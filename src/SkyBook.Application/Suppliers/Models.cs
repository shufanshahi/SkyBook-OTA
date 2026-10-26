namespace SkyBook.Application.Suppliers;

public record FlightSearchRequest(
    string Origin, string Destination, DateOnly DepartureDate,
    int Adults = 1, int Children = 0, int Infants = 0, string Cabin = "Economy");

public record Segment(
    string Carrier, string FlightNo, string Origin, string Destination,
    DateTimeOffset DepartureUtc, DateTimeOffset ArrivalUtc);

/// <summary>Supplier-independent offer. OfferId is prefixed with the supplier name, e.g. "SkyGDS:GDS-1a2b-0".</summary>
public record FlightOffer(
    string OfferId,
    string Supplier,
    IReadOnlyList<Segment> Segments,
    decimal BaseFare, decimal Taxes, decimal Total,
    string Currency, string CabinClass, int SeatsAvailable,
    string BaggageAllowance, bool Refundable,
    DateTimeOffset ExpiresAt);

public record PricedOffer(FlightOffer Offer);

public record SupplierPassenger(
    string Type, string Title, string FirstName, string LastName,
    DateOnly DateOfBirth, string? PassportNo);

public record SupplierBookingRequest(string OfferId, IReadOnlyList<SupplierPassenger> Passengers);
public record SupplierBookingResult(string SupplierRef, string Pnr, decimal Total, string Currency);
public record SupplierTicketResult(string SupplierRef, IReadOnlyList<string> TicketNumbers);
public record SupplierCancelResult(string SupplierRef, string Status);

public record SupplierWarning(string Supplier, string Message);
public record SearchResult(IReadOnlyList<FlightOffer> Offers, IReadOnlyList<SupplierWarning> Warnings);

public record PriceCheckResult(FlightOffer Offer, decimal PreviousTotal, decimal NewTotal)
{
    public bool PriceChanged => PreviousTotal != NewTotal;
}

/// <summary>A supplier call failed (timeout, fault, HTTP error). Maps to a "502 bad gateway" style message in the UI.</summary>
public class SupplierException(string supplier, string message, string? code = null, Exception? inner = null)
    : Exception($"{supplier}: {message}", inner)
{
    public string Supplier { get; } = supplier;
    public string? Code { get; } = code;
}

/// <summary>The offer is unknown or expired in our cache / at the supplier: the agent must search again.</summary>
public class OfferUnavailableException(string message) : Exception(message);
