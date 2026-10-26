using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using SkyBook.Application.Suppliers;

namespace SkyBook.Web.Models;

public class FlightSearchViewModel : IValidatableObject
{
    [Required, StringLength(3, MinimumLength = 3), Display(Name = "From")] public string Origin { get; set; } = "DAC";
    [Required, StringLength(3, MinimumLength = 3), Display(Name = "To")] public string Destination { get; set; } = "CGP";
    [Required, DataType(DataType.Date), Display(Name = "Departure date")] public DateTime? DepartureDate { get; set; } = DateTime.Today.AddDays(14);
    [Range(1, 9)] public int Adults { get; set; } = 1;
    [Range(0, 9)] public int Children { get; set; }
    [Range(0, 9)] public int Infants { get; set; }
    public string Cabin { get; set; } = "Economy";

    [BindNever] public SearchResult? Result { get; set; }
    [BindNever] public bool Searched { get; set; }

    public static IEnumerable<string> Airports => Application.Suppliers.Airports.All;
    public static readonly string[] Cabins = ["Economy", "Business"];

    public FlightSearchRequest ToRequest() =>
        new(Origin.ToUpperInvariant(), Destination.ToUpperInvariant(), DateOnly.FromDateTime(DepartureDate!.Value), Adults, Children, Infants, Cabin);

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.Equals(Origin, Destination, StringComparison.OrdinalIgnoreCase))
            yield return new ValidationResult("Origin and destination must be different.", [nameof(Destination)]);
        if (DepartureDate is { } d && d.Date < DateTime.Today)
            yield return new ValidationResult("Departure date cannot be in the past.", [nameof(DepartureDate)]);
        if (Infants > Adults)
            yield return new ValidationResult("There cannot be more infants than adults.", [nameof(Infants)]);
        if (Adults + Children + Infants > 9)
            yield return new ValidationResult("At most 9 passengers per booking.", [nameof(Adults)]);
        if (!Application.Suppliers.Airports.All.Contains(Origin.ToUpperInvariant()))
            yield return new ValidationResult("Unknown origin airport.", [nameof(Origin)]);
        if (!Application.Suppliers.Airports.All.Contains(Destination.ToUpperInvariant()))
            yield return new ValidationResult("Unknown destination airport.", [nameof(Destination)]);
    }
}
