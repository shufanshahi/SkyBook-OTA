using System.ComponentModel.DataAnnotations;
using SkyBook.Domain.Entities;
using SkyBook.Infrastructure.Queries;

namespace SkyBook.Web.Areas.Admin.Models;

public class AgentCreateViewModel
{
    [Required, StringLength(150), Display(Name = "Agency name")] public string AgencyName { get; set; } = "";
    [Required, StringLength(20), RegularExpression("^[A-Za-z0-9]+$", ErrorMessage = "Letters and digits only.")] public string Code { get; set; } = "";
    [Required, EmailAddress, Display(Name = "Login email")] public string Email { get; set; } = "";
    [Required, MinLength(8), DataType(DataType.Password), Display(Name = "Initial password")] public string Password { get; set; } = "";
    [Range(0, 50), Display(Name = "Markup %")] public decimal MarkupPercent { get; set; }
    [Range(0, 10_000_000), Display(Name = "Credit limit")] public decimal CreditLimit { get; set; }
}

public class AgentListViewModel
{
    public string? Search { get; set; }
    public string? Status { get; set; }
    public IReadOnlyList<Agent> Agents { get; set; } = [];
}

public class AdminBookingSearchViewModel
{
    public int? AgentId { get; set; }
    public string? Status { get; set; }
    public string? Pnr { get; set; }
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public bool Searched { get; set; }
    public IReadOnlyList<Agent> Agents { get; set; } = [];
    public IReadOnlyList<BookingRow> Rows { get; set; } = [];
    public int TotalCount => Rows.Count > 0 ? Rows[0].TotalCount : 0;
    public bool HasNext => Page * PageSize < TotalCount;
}

public class SalesReportViewModel
{
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
    public IReadOnlyList<SalesRow> Rows { get; set; } = [];
}

public class SupplierLogViewModel
{
    public string? CorrelationId { get; set; }
    public string? Supplier { get; set; }
    public IReadOnlyList<SupplierLog> Logs { get; set; } = [];
}
