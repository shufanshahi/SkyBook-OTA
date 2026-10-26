using Microsoft.Extensions.Caching.Memory;
using SkyBook.Application.Suppliers;

namespace SkyBook.Infrastructure.Suppliers;

public sealed class MemoryOfferCache(IMemoryCache cache) : IOfferCache
{
    private static string Key(string offerId) => $"offer:{offerId}";

    public Task StoreAsync(IEnumerable<FlightOffer> offers, TimeSpan ttl)
    {
        foreach (var o in offers) cache.Set(Key(o.OfferId), o, ttl);
        return Task.CompletedTask;
    }

    public Task<FlightOffer?> GetAsync(string offerId) =>
        Task.FromResult(cache.TryGetValue(Key(offerId), out FlightOffer? o) ? o : null);
}
