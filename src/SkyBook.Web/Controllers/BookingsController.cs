using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyBook.Application.Bookings;
using SkyBook.Application.Suppliers;
using SkyBook.Domain;
using SkyBook.Domain.Enums;
using SkyBook.Infrastructure.Bookings;
using SkyBook.Web.Models;

namespace SkyBook.Web.Controllers;

[Authorize(Roles = "Agent")]
public class BookingsController(BookingService bookings, FlightPricingService pricing, AdminService admin, IOfferCache cache)
    : AgentControllerBase
{
    private static bool IsExpected(Exception ex) =>
        ex is DomainException or SupplierException or OfferUnavailableException or ValidationFailedException;

    // ---- list + details ---------------------------------------------------------------

    public async Task<IActionResult> Index(string? status, int page = 1, CancellationToken ct = default)
    {
        var vm = new BookingListViewModel { Status = status, Page = Math.Max(1, page) };
        vm.Rows = await bookings.ListAsync(AgentId, status, vm.Page, vm.PageSize, ct);
        return View(vm);
    }

    [HttpGet("Bookings/Details/{pnr}")]
    public async Task<IActionResult> Details(string pnr, CancellationToken ct)
    {
        var booking = await bookings.GetAsync(AgentId, pnr, ct);
        if (booking is null) return NotFound();      // also what another agent's PNR looks like: 404, never "forbidden"

        return View(new BookingDetailsViewModel
        {
            Booking = booking,
            Refund = await bookings.GetRefundAsync(booking.Id, ct),
            CanIssue = BookingTransitions.CanMove(booking.Status, BookingStatus.Ticketed),
            CanCancel = BookingTransitions.CanMove(booking.Status, BookingStatus.Cancelled),
            HoldExpired = booking.Status == BookingStatus.Held && booking.HoldExpiresAt < DateTime.UtcNow
        });
    }

    // ---- create (review price + passengers) ---------------------------------------------

    [HttpGet]
    public async Task<IActionResult> Create(string offerId, int adults = 1, int children = 0, int infants = 0,
        decimal? seenTotal = null, CancellationToken ct = default)
    {
        try
        {
            var agent = await admin.GetAgentAsync(AgentId, ct);
            var check = await pricing.PriceAsync(offerId, agent!.MarkupPercent, ct);   // re-price with the supplier

            var vm = new BookingCreateViewModel { OfferId = offerId, ExpectedTotal = check.NewTotal };
            vm.PriceChanged = seenTotal is { } seen && seen != check.NewTotal;
            vm.PreviousTotal = vm.PriceChanged ? seenTotal : null;
            vm.Passengers.AddRange(Enumerable.Repeat("ADT", adults).Concat(Enumerable.Repeat("CHD", children))
                .Concat(Enumerable.Repeat("INF", infants)).Select(t => new PassengerInputModel { Type = t }));
            Fill(vm, check.Offer);
            return View(vm);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction("Search", "Flights");
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(BookingCreateViewModel vm, CancellationToken ct)
    {
        var offer = await cache.GetAsync(vm.OfferId);
        if (offer is null)
        {
            TempData["Error"] = "This offer is no longer available. Please search again.";
            return RedirectToAction("Search", "Flights");
        }
        Fill(vm, offer);
        if (!ModelState.IsValid) return View(vm);

        var pax = vm.Passengers.Select(p => p.ToSupplierPassenger()).ToList();
        foreach (var e in PassengerRules.Validate(pax, vm.TravelDate, vm.International)) ModelState.AddModelError("", e);
        if (!ModelState.IsValid) return View(vm);

        try
        {
            var pnr = await bookings.CreateAsync(AgentId, vm.OfferId, vm.ExpectedTotal, vm.IdempotencyKey, pax, ct);
            TempData["Success"] = $"Booking created. PNR {pnr}. Issue the ticket before the hold expires.";
            return RedirectToAction(nameof(Details), new { pnr });         // Post/Redirect/Get: refreshing cannot re-book
        }
        catch (PriceChangedException ex)
        {
            vm.PreviousTotal = vm.ExpectedTotal;
            vm.ExpectedTotal = ex.NewTotal;
            vm.PriceChanged = true;
            ModelState.Remove(nameof(vm.ExpectedTotal));                    // otherwise the old posted value is re-rendered
            return View(vm);
        }
        catch (ValidationFailedException ex)
        {
            foreach (var e in ex.Errors) ModelState.AddModelError("", e);
            return View(vm);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            ModelState.AddModelError("", ex.Message);
            return View(vm);
        }
    }

    private static void Fill(BookingCreateViewModel vm, FlightOffer offer)
    {
        var seg = offer.Segments[0];
        vm.Offer = offer;
        vm.TravelDate = DateOnly.FromDateTime(seg.DepartureUtc.UtcDateTime);
        vm.International = Airports.IsInternational(seg.Origin, seg.Destination);
    }

    // ---- state changes (always POST) ----------------------------------------------------

    [HttpPost("Bookings/Ticket/{pnr}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Ticket(string pnr, CancellationToken ct)
    {
        try
        {
            var numbers = await bookings.IssueTicketAsync(AgentId, pnr, ct);
            TempData["Success"] = $"Ticket issued: {string.Join(", ", numbers)}.";
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (IsExpected(ex)) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { pnr });
    }

    [HttpPost("Bookings/Cancel/{pnr}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string pnr, CancellationToken ct)
    {
        try
        {
            var refund = await bookings.CancelAsync(AgentId, pnr, ct);
            TempData["Success"] = refund is null
                ? "Booking cancelled."
                : $"Booking cancelled. A refund of BDT {refund.RefundAmount:N0} (penalty {refund.Penalty:N0}) is pending admin approval.";
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (Exception ex) when (IsExpected(ex)) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Details), new { pnr });
    }
}
