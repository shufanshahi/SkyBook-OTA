namespace SkyBook.Application.Suppliers;

/// <summary>Re-prices a previously searched offer with the supplier that owns it and reports whether the fare moved.</summary>
public sealed class FlightPricingService(IEnumerable<IFlightSupplier> suppliers, IOfferCache cache)
{
    public async Task<PriceCheckResult> PriceAsync(string offerId, decimal markupPercent, CancellationToken ct)
    {
        var cached = await cache.GetAsync(offerId)
            ?? throw new OfferUnavailableException("This offer is no longer available. Please search again.");
        if (cached.ExpiresAt < DateTimeOffset.UtcNow)
            throw new OfferUnavailableException("This offer has expired. Please search again.");

        var supplier = suppliers.FirstOrDefault(s => s.Name == cached.Supplier)
            ?? throw new OfferUnavailableException($"Supplier {cached.Supplier} is not available.");

        var fresh = (await supplier.PriceAsync(offerId, ct)).Offer;
        await cache.StoreAsync([fresh], FlightSearchService.OfferTtl);

        var before = PricingPolicy.ApplyMarkup(cached, markupPercent);
        var after = PricingPolicy.ApplyMarkup(fresh, markupPercent);
        return new PriceCheckResult(after, before.Total, after.Total);
    }
}
