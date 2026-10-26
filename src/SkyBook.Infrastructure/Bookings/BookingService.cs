using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SkyBook.Application.Bookings;
using SkyBook.Application.Suppliers;
using SkyBook.Domain;
using SkyBook.Domain.Entities;
using SkyBook.Domain.Enums;
using SkyBook.Infrastructure.Persistence;
using SkyBook.Infrastructure.Queries;

namespace SkyBook.Infrastructure.Bookings;

/// <summary>The booking lifecycle: create (hold), ticket, cancel, expire. Every method is scoped to the calling agent.</summary>
public sealed class BookingService(
    SkyBookDbContext db,
    FlightPricingService pricing,
    IEnumerable<IFlightSupplier> suppliers,
    IOfferCache cache,
    BookingQueries sql,
    ILogger<BookingService> log)
{
    public static readonly TimeSpan HoldDuration = TimeSpan.FromMinutes(30);

    private IFlightSupplier SupplierFor(string name) =>
        suppliers.FirstOrDefault(s => s.Name == name) ?? throw new DomainException($"Supplier {name} is not configured.");

    // ---- create --------------------------------------------------------------------------

    /// <param name="expectedTotal">Per-passenger total the agent saw on screen; if the supplier price moved, nothing is booked.</param>
    /// <returns>The PNR (the existing one when the same idempotency key is replayed).</returns>
    public async Task<string> CreateAsync(int agentId, string offerId, decimal expectedTotal, Guid idempotencyKey,
        IReadOnlyList<SupplierPassenger> pax, CancellationToken ct)
    {
        var existing = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
            return existing.AgentId == agentId ? existing.Pnr : throw new DomainException("Invalid booking request.");

        var agent = await db.Agents.FindAsync([agentId], ct) ?? throw new DomainException("Agent not found.");
        if (agent.Status != "Active") throw new DomainException("Your agency is suspended and cannot make bookings.");

        // re-price with the owning supplier right before booking
        var check = await pricing.PriceAsync(offerId, agent.MarkupPercent, ct);
        if (check.NewTotal != expectedTotal) throw new PriceChangedException(check.NewTotal);

        var offer = check.Offer;
        var seg = offer.Segments[0];
        var travelDate = DateOnly.FromDateTime(seg.DepartureUtc.UtcDateTime);
        var errors = PassengerRules.Validate(pax, travelDate, Airports.IsInternational(seg.Origin, seg.Destination));
        if (errors.Count > 0) throw new ValidationFailedException(errors);

        // Book is NOT retried (separate no-retry HTTP client): a retry could create a second booking at the supplier
        var supplierResult = await SupplierFor(offer.Supplier).BookAsync(new SupplierBookingRequest(offerId, pax), ct);

        var booking = new Booking
        {
            Pnr = await NewPnrAsync(ct),
            AgentId = agentId,
            Supplier = offer.Supplier,
            SupplierRef = supplierResult.SupplierRef,
            Status = BookingStatus.Held,
            TotalFare = offer.Total * pax.Count,
            Currency = offer.Currency,
            OfferId = offerId,
            HoldExpiresAt = DateTime.UtcNow + HoldDuration,
            IdempotencyKey = idempotencyKey,
            Segments = offer.Segments.Select(s => new BookingSegment
            {
                Carrier = s.Carrier, FlightNo = s.FlightNo, Origin = s.Origin, Destination = s.Destination,
                DepartureUtc = s.DepartureUtc.UtcDateTime, ArrivalUtc = s.ArrivalUtc.UtcDateTime, CabinClass = offer.CabinClass
            }).ToList(),
            Passengers = pax.Select(p => new Passenger
            {
                Type = Enum.Parse<PassengerType>(p.Type), Title = p.Title, FirstName = p.FirstName,
                LastName = p.LastName, DateOfBirth = p.DateOfBirth, PassportNo = p.PassportNo
            }).ToList()
        };

        try
        {
            db.Bookings.Add(booking);
            await db.SaveChangesAsync(ct);
            return booking.Pnr;
        }
        catch (DbUpdateException ex)
        {
            // two simultaneous submits with the same key: the other request won, return its booking
            var winner = await db.Bookings.AsNoTracking().FirstOrDefaultAsync(b => b.IdempotencyKey == idempotencyKey, ct);
            if (winner is not null && winner.AgentId == agentId)
            {
                await TryCancelAtSupplierAsync(offer.Supplier, supplierResult.SupplierRef, ct);
                return winner.Pnr;
            }

            log.LogError(ex, "Booked at {Supplier} ({Ref}) but saving locally failed; cancelling at the supplier",
                offer.Supplier, supplierResult.SupplierRef);
            await TryCancelAtSupplierAsync(offer.Supplier, supplierResult.SupplierRef, ct);
            throw;
        }
    }

    private async Task<string> NewPnrAsync(CancellationToken ct)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var pnr = new string(Enumerable.Range(0, 6).Select(_ => alphabet[Random.Shared.Next(alphabet.Length)]).ToArray());
            if (!await db.Bookings.AnyAsync(b => b.Pnr == pnr, ct)) return pnr;
        }
        throw new InvalidOperationException("Could not generate a unique PNR.");
    }

    private async Task TryCancelAtSupplierAsync(string supplier, string supplierRef, CancellationToken ct)
    {
        try { await SupplierFor(supplier).CancelAsync(supplierRef, ct); }
        catch (Exception ex) { log.LogError(ex, "Could not cancel {Supplier} booking {Ref}; needs manual cleanup", supplier, supplierRef); }
    }

    // ---- read ----------------------------------------------------------------------------

    public Task<Booking?> GetAsync(int agentId, string pnr, CancellationToken ct) =>
        db.Bookings.AsNoTracking().AsSplitQuery()
            .Include(b => b.Segments).Include(b => b.Passengers).Include(b => b.Tickets)
            .FirstOrDefaultAsync(b => b.Pnr == pnr && b.AgentId == agentId, ct);

    public Task<IReadOnlyList<BookingRow>> ListAsync(int agentId, string? status, int page, int pageSize, CancellationToken ct) =>
        sql.SearchAsync(new BookingSearchFilter(AgentId: agentId, Status: status, Page: page, PageSize: pageSize), ct);

    public Task<Refund?> GetRefundAsync(long bookingId, CancellationToken ct) =>
        db.Refunds.AsNoTracking().FirstOrDefaultAsync(r => r.BookingId == bookingId, ct);

    // ---- ticket --------------------------------------------------------------------------

    public async Task<IReadOnlyList<string>> IssueTicketAsync(int agentId, string pnr, CancellationToken ct)
    {
        var b = await LoadTrackedAsync(agentId, pnr, ct);
        if (!BookingTransitions.CanMove(b.Status, BookingStatus.Ticketed))
            throw new DomainException($"Booking {pnr} is {b.Status} and cannot be ticketed.");
        if (b.HoldExpiresAt is { } hold && hold < DateTime.UtcNow)
            throw new DomainException($"The hold on booking {pnr} has expired.");

        // cheap pre-check so we do not ticket at the supplier just to fail on money afterwards
        var agent = await db.Agents.AsNoTracking().FirstAsync(a => a.Id == agentId, ct);
        if (agent.Status != "Active") throw new DomainException("Your agency is suspended.");
        if (agent.WalletBalance + agent.CreditLimit < b.TotalFare) throw new DomainException("Insufficient wallet balance.");

        // 1) supplier first (not retried)...
        var result = await SupplierFor(b.Supplier).IssueTicketAsync(b.SupplierRef, ct);

        // 2) ...then the atomic wallet debit + status change in SQL
        try { await sql.IssueTicketAsync(b.Id, agentId, ct); }
        catch (Exception ex)
        {
            // Trade-off: the airline already holds a ticket but our books did not move. Make it loud for manual reconciliation.
            log.LogCritical(ex, "RECONCILE: {Supplier} ticketed {Pnr} (ref {Ref}) but usp_IssueTicket failed", b.Supplier, pnr, b.SupplierRef);
            throw new DomainException($"{ex.Message} The supplier may already have ticketed this booking; quote PNR {pnr} to support.");
        }

        await db.Entry(b).ReloadAsync(ct);
        for (var i = 0; i < b.Passengers.Count; i++)
        {
            db.Tickets.Add(new Ticket
            {
                BookingId = b.Id, PassengerId = b.Passengers[i].Id,
                TicketNumber = i < result.TicketNumbers.Count ? result.TicketNumbers[i] : $"{result.SupplierRef}-{i + 1}"
            });
        }
        await db.SaveChangesAsync(ct);
        return result.TicketNumbers;
    }

    // ---- cancel --------------------------------------------------------------------------

    /// <returns>The pending refund when a ticketed booking was cancelled, otherwise null.</returns>
    public async Task<Refund?> CancelAsync(int agentId, string pnr, CancellationToken ct)
    {
        var b = await LoadTrackedAsync(agentId, pnr, ct);
        if (!BookingTransitions.CanMove(b.Status, BookingStatus.Cancelled))
            throw new DomainException($"Booking {pnr} is {b.Status} and cannot be cancelled.");

        await SupplierFor(b.Supplier).CancelAsync(b.SupplierRef, ct);
        await sql.CancelBookingAsync(b.Id, agentId, ct);
        return await GetRefundAsync(b.Id, ct);
    }

    private async Task<Booking> LoadTrackedAsync(int agentId, string pnr, CancellationToken ct) =>
        await db.Bookings.Include(b => b.Passengers).FirstOrDefaultAsync(b => b.Pnr == pnr && b.AgentId == agentId, ct)
            ?? throw new KeyNotFoundException($"Booking {pnr} was not found.");

    // ---- hold expiry (called by the background job) ----------------------------------------

    public async Task<int> ExpireHeldAsync(CancellationToken ct)
    {
        var due = await db.Bookings
            .Where(b => b.Status == BookingStatus.Held && b.HoldExpiresAt < DateTime.UtcNow)   // served by IX_Bookings_Status_HoldExpires
            .OrderBy(b => b.HoldExpiresAt).Take(50).ToListAsync(ct);

        foreach (var b in due)
        {
            await TryCancelAtSupplierAsync(b.Supplier, b.SupplierRef, ct);
            b.MoveTo(BookingStatus.Expired);
        }
        await db.SaveChangesAsync(ct);
        if (due.Count > 0) log.LogInformation("Expired {Count} held booking(s)", due.Count);
        return due.Count;
    }
}
