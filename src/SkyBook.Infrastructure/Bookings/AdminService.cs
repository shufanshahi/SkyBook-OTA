using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkyBook.Domain;
using SkyBook.Domain.Entities;
using SkyBook.Domain.Enums;
using SkyBook.Infrastructure.Persistence;

namespace SkyBook.Infrastructure.Bookings;

public record RefundRow(long Id, string Pnr, string AgencyName, decimal RefundAmount, decimal Penalty, RefundStatus Status, DateTime? ProcessedAt);

/// <summary>Back-office operations: agents, wallets, markup, refunds approval and the supplier-log troubleshooting view.</summary>
public sealed class AdminService(SkyBookDbContext db, IPasswordHasher<User> hasher)
{
    public Task<Agent?> GetAgentAsync(int id, CancellationToken ct) =>
        db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<IReadOnlyList<Agent>> ListAgentsAsync(string? search, string? status, CancellationToken ct)
    {
        var q = db.Agents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(a => a.AgencyName.Contains(search) || a.Code.Contains(search));
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(a => a.Status == status);
        return await q.OrderBy(a => a.AgencyName).ToListAsync(ct);
    }

    public async Task<Agent> CreateAgentAsync(string agencyName, string code, string email, string password,
        decimal markupPercent, decimal creditLimit, CancellationToken ct)
    {
        code = code.Trim().ToUpperInvariant();
        email = email.Trim().ToLowerInvariant();
        if (await db.Agents.AnyAsync(a => a.Code == code, ct)) throw new DomainException($"Agent code {code} is already used.");
        if (await db.Users.AnyAsync(u => u.Email == email, ct)) throw new DomainException($"{email} already has a login.");
        if (markupPercent is < 0 or > 50) throw new DomainException("Markup must be between 0 and 50 percent.");

        var agent = new Agent { AgencyName = agencyName.Trim(), Code = code, MarkupPercent = markupPercent, CreditLimit = creditLimit };
        var user = new User { Email = email, Role = "Agent", Agent = agent };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);                       // saves the agent too (navigation)
        await db.SaveChangesAsync(ct);
        return agent;
    }

    public Task SetMarkupAsync(int agentId, decimal markupPercent, CancellationToken ct)
    {
        if (markupPercent is < 0 or > 50) throw new DomainException("Markup must be between 0 and 50 percent.");
        return UpdateAgentAsync(agentId, a => a.MarkupPercent = markupPercent, ct);
    }

    public Task SetStatusAsync(int agentId, bool active, CancellationToken ct) =>
        UpdateAgentAsync(agentId, a => a.Status = active ? "Active" : "Suspended", ct);

    public async Task TopUpAsync(int agentId, decimal amount, string reference, CancellationToken ct)
    {
        if (amount <= 0) throw new DomainException("Top-up amount must be positive.");
        await UpdateAgentAsync(agentId, a =>
        {
            a.WalletBalance += amount;
            db.WalletTransactions.Add(new WalletTransaction
            {
                AgentId = a.Id, Amount = amount, Type = WalletTransactionType.TopUp,
                Reference = string.IsNullOrWhiteSpace(reference) ? "Admin top-up" : reference.Trim()
            });
        }, ct);
    }

    private async Task UpdateAgentAsync(int agentId, Action<Agent> change, CancellationToken ct)
    {
        var agent = await db.Agents.FindAsync([agentId], ct) ?? throw new KeyNotFoundException("Agent not found.");
        change(agent);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)    // Agent.RowVersion changed under us (another admin, or a ticket debit)
        {
            throw new DomainException("This agent was changed by someone else. Reload and try again.");
        }
    }

    // ---- refunds -------------------------------------------------------------------------

    public async Task<IReadOnlyList<RefundRow>> ListRefundsAsync(bool pendingOnly, CancellationToken ct) =>
        await (from r in db.Refunds.AsNoTracking()
               join b in db.Bookings.AsNoTracking() on r.BookingId equals b.Id
               join a in db.Agents.AsNoTracking() on b.AgentId equals a.Id
               where !pendingOnly || r.Status == RefundStatus.Pending
               orderby r.Id descending
               select new RefundRow(r.Id, b.Pnr, a.AgencyName, r.RefundAmount, r.Penalty, r.Status, r.ProcessedAt))
              .Take(100).ToListAsync(ct);

    /// <summary>Credits the agent wallet, marks the booking Refunded and the refund Approved, all in one transaction.</summary>
    public async Task ApproveRefundAsync(long refundId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var refund = await db.Refunds.FirstOrDefaultAsync(r => r.Id == refundId, ct) ?? throw new KeyNotFoundException("Refund not found.");
        if (refund.Status != RefundStatus.Pending) throw new DomainException("This refund has already been processed.");

        var booking = await db.Bookings.FirstAsync(b => b.Id == refund.BookingId, ct);
        var agent = await db.Agents.FirstAsync(a => a.Id == booking.AgentId, ct);

        booking.MoveTo(BookingStatus.Refunded);              // Cancelled -> Refunded, or a DomainException
        agent.WalletBalance += refund.RefundAmount;
        db.WalletTransactions.Add(new WalletTransaction
        {
            AgentId = agent.Id, Amount = refund.RefundAmount, Type = WalletTransactionType.RefundCredit, Reference = booking.Pnr
        });
        refund.Status = RefundStatus.Approved;
        refund.ProcessedAt = DateTime.UtcNow;

        try
        {
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new DomainException("The agent's wallet changed while approving. Please try again.");
        }
    }

    // ---- troubleshooting -----------------------------------------------------------------

    public async Task<IReadOnlyList<SupplierLog>> SupplierLogsAsync(string? correlationId, string? supplier, CancellationToken ct)
    {
        var q = db.SupplierLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(correlationId)) q = q.Where(l => l.CorrelationId == correlationId.Trim());
        if (!string.IsNullOrWhiteSpace(supplier)) q = q.Where(l => l.Supplier == supplier);
        return await q.OrderByDescending(l => l.Id).Take(100).ToListAsync(ct);
    }

    public async Task<(int Agents, int BookingsToday, int PendingRefunds)> DashboardAsync(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        return (await db.Agents.CountAsync(ct),
                await db.Bookings.CountAsync(b => b.CreatedAt >= today, ct),
                await db.Refunds.CountAsync(r => r.Status == RefundStatus.Pending, ct));
    }
}
