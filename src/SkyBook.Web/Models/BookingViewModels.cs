using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SkyBook.Application.Suppliers;
using SkyBook.Domain.Entities;
using SkyBook.Infrastructure.Queries;

namespace SkyBook.Web.Models;

public class PassengerInputModel
{
    public string Type { get; set; } = "ADT";
    public string Title { get; set; } = "Mr";
    [Required] public string FirstName { get; set; } = "";
    [Required] public string LastName { get; set; } = "";
    [Required, DataType(DataType.Date)] public DateOnly? DateOfBirth { get; set; }
    public string? PassportNo { get; set; }

    public SupplierPassenger ToSupplierPassenger() =>
        new(Type, Title, FirstName.Trim(), LastName.Trim(), DateOfBirth!.Value, string.IsNullOrWhiteSpace(PassportNo) ? null : PassportNo.Trim());
}

public class BookingCreateViewModel
{
    [Required] public string OfferId { get; set; } = "";
    public decimal ExpectedTotal { get; set; }                 // per-passenger total the agent is agreeing to
    public Guid IdempotencyKey { get; set; } = Guid.NewGuid(); // same key = same booking, even if the form is posted twice
    public List<PassengerInputModel> Passengers { get; set; } = [];

    // refilled on every render, never posted
    [BindNever] public FlightOffer? Offer { get; set; }
    [BindNever] public bool PriceChanged { get; set; }
    [BindNever] public decimal? PreviousTotal { get; set; }
    [BindNever] public bool International { get; set; }
    [BindNever] public DateOnly TravelDate { get; set; }
}

public class BookingListViewModel
{
    public IReadOnlyList<BookingRow> Rows { get; set; } = [];
    public string? Status { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalCount => Rows.Count > 0 ? Rows[0].TotalCount : 0;
    public bool HasNext => Page * PageSize < TotalCount;
}

public class BookingDetailsViewModel
{
    public Booking Booking { get; set; } = null!;
    public Refund? Refund { get; set; }
    public bool CanIssue { get; set; }
    public bool CanCancel { get; set; }
    public bool HoldExpired { get; set; }
}
