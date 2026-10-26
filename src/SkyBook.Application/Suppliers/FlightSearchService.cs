using Microsoft.Extensions.Logging;

namespace SkyBook.Application.Suppliers;

/// <summary>Searches every supplier in parallel and merges the results. One supplier failing never fails the search.</summary>
public sealed class FlightSearchService(
    IEnumerable<IFlightSupplier> suppliers,
    IOfferCache cache,
    ILogger<FlightSearchService> log,
    TimeSpan? supplierTimeout = null)
{
    public static readonly TimeSpan OfferTtl = TimeSpan.FromMinutes(15);
    private readonly TimeSpan _timeout = supplierTimeout ?? TimeSpan.FromSeconds(6);

    public Task<SearchResult> SearchAsync(FlightSearchRequest req, CancellationToken ct) =>
        SearchAsync(req, 0m, ct);

    public async Task<SearchResult> SearchAsync(FlightSearchRequest req, decimal markupPercent, CancellationToken ct)
    {
        var tasks = suppliers.Select(async s =>
        {
            // each supplier gets its own deadline so a slow one cannot hold the whole page hostage
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);
            try
            {
                return (s.Name, Offers: await s.SearchAsync(req, cts.Token), Error: (string?)null);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                log.LogWarning(ex, "Supplier {Supplier} search failed", s.Name);
                var message = ex is OperationCanceledException ? "timed out" : ex.Message;
                return (s.Name, Offers: (IReadOnlyList<FlightOffer>)[], Error: message);
            }
        });

        var results = await Task.WhenAll(tasks);

        var raw = results.SelectMany(r => r.Offers).ToList();
        await cache.StoreAsync(raw, OfferTtl); // supplier prices; needed for Price/Book later

        var offers = raw.Select(o => PricingPolicy.ApplyMarkup(o, markupPercent))
                        .OrderBy(o => o.Total)
                        .ToList();

        var warnings = results.Where(r => r.Error != null)
                              .Select(r => new SupplierWarning(r.Name, r.Error!))
                              .ToList();
        return new SearchResult(offers, warnings);
    }
}
