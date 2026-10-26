namespace SkyBook.Application.Suppliers;

public interface IFlightSupplier
{
    string Name { get; }
    Task<IReadOnlyList<FlightOffer>> SearchAsync(FlightSearchRequest req, CancellationToken ct);
    Task<PricedOffer> PriceAsync(string offerId, CancellationToken ct);
    Task<SupplierBookingResult> BookAsync(SupplierBookingRequest req, CancellationToken ct);
    Task<SupplierTicketResult> IssueTicketAsync(string supplierRef, CancellationToken ct);
    Task<SupplierCancelResult> CancelAsync(string supplierRef, CancellationToken ct);
}

/// <summary>Remembers offers from a search so Price/Book can find which supplier owns them. Stores supplier (raw) prices.</summary>
public interface IOfferCache
{
    Task StoreAsync(IEnumerable<FlightOffer> offers, TimeSpan ttl);
    Task<FlightOffer?> GetAsync(string offerId);
}

public static class OfferIds
{
    public static string Prefix(string supplier, string rawId) => $"{supplier}:{rawId}";

    /// <summary>"SkyGDS:GDS-1a2b-0" -> "GDS-1a2b-0"</summary>
    public static string Raw(string supplier, string offerId) =>
        offerId.StartsWith(supplier + ":", StringComparison.Ordinal) ? offerId[(supplier.Length + 1)..] : offerId;
}
