using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SkyBook.Domain;

namespace SkyBook.Infrastructure.Queries;

public record BookingSearchFilter(
    int? AgentId = null, string? Status = null, string? Pnr = null,
    DateTime? FromDate = null, DateTime? ToDate = null, int Page = 1, int PageSize = 20);

public record BookingRow(
    long Id, string Pnr, string AgencyName, string Supplier, string Status,
    decimal TotalFare, DateTime CreatedAt, int TotalCount);

public record SalesRow(
    int AgentId, string AgencyName, DateTime Month,
    int BookingsTicketed, decimal GrossSales, decimal Refunds, decimal NetSales);

/// <summary>Read-side queries that go through stored procedures via Dapper.</summary>
public class BookingQueries(IConfiguration config)
{
    private SqlConnection Open() => new(config.GetConnectionString("Default"));

    public async Task<IReadOnlyList<BookingRow>> SearchAsync(BookingSearchFilter f, CancellationToken ct)
    {
        await using var conn = Open();
        var rows = await conn.QueryAsync<BookingRow>(new CommandDefinition(
            "dbo.usp_SearchBookings",
            new { f.AgentId, f.Status, f.Pnr, f.FromDate, f.ToDate, f.Page, f.PageSize },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
        return rows.AsList();
    }

    public async Task<IReadOnlyList<SalesRow>> SalesReportAsync(DateTime? from, DateTime? to, CancellationToken ct)
    {
        await using var conn = Open();
        var rows = await conn.QueryAsync<SalesRow>(new CommandDefinition(
            "dbo.usp_AgentSalesReport",
            new { FromDate = from, ToDate = to },
            commandType: CommandType.StoredProcedure,
            cancellationToken: ct));
        return rows.AsList();
    }

    /// <summary>Atomic wallet debit + status change (usp_IssueTicket). Raises DomainException for the SP's business errors.</summary>
    public Task IssueTicketAsync(long bookingId, int agentId, CancellationToken ct) =>
        ExecuteAsync("dbo.usp_IssueTicket", new { BookingId = bookingId, AgentId = agentId }, ct);

    /// <summary>Cancels a Held/Ticketed booking and queues a pending refund when it had been ticketed (usp_CancelBooking).</summary>
    public Task CancelBookingAsync(long bookingId, int agentId, CancellationToken ct) =>
        ExecuteAsync("dbo.usp_CancelBooking", new { BookingId = bookingId, AgentId = agentId }, ct);

    private async Task ExecuteAsync(string procedure, object args, CancellationToken ct)
    {
        await using var conn = Open();
        try
        {
            await conn.ExecuteAsync(new CommandDefinition(procedure, args,
                commandType: CommandType.StoredProcedure, cancellationToken: ct));
        }
        catch (SqlException ex) when (ex.Number is >= 50001 and <= 50099)
        {
            // THROW numbers 50001+ are our own business errors (not found, wrong status, insufficient balance)
            throw new DomainException(ex.Message);
        }
    }
}
