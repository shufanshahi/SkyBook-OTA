using FluentAssertions;
using NSubstitute;
using SkyBook.Application.Suppliers;

namespace SkyBook.UnitTests.Suppliers;

public class FlightPricingServiceTests
{
    [Fact]
    public async Task Reports_price_change_with_agent_markup_applied()
    {
        var cache = new FakeOfferCache();
        await cache.StoreAsync([TestData.Offer(5000, "SkyGDS", "a")], TimeSpan.FromMinutes(5));
        var supplier = Substitute.For<IFlightSupplier>();
        supplier.Name.Returns("SkyGDS");
        supplier.PriceAsync("SkyGDS:a", default).ReturnsForAnyArgs(new PricedOffer(TestData.Offer(5200, "SkyGDS", "a")));

        var result = await new FlightPricingService([supplier], cache).PriceAsync("SkyGDS:a", 10m, CancellationToken.None);

        result.PriceChanged.Should().BeTrue();
        result.PreviousTotal.Should().Be(5500m);
        result.NewTotal.Should().Be(5720m);
        (await cache.GetAsync("SkyGDS:a"))!.Total.Should().Be(5200m);   // cache refreshed with the new supplier price
    }

    [Fact]
    public async Task Same_price_is_not_a_change()
    {
        var cache = new FakeOfferCache();
        await cache.StoreAsync([TestData.Offer(5000, "SkyGDS", "a")], TimeSpan.FromMinutes(5));
        var supplier = Substitute.For<IFlightSupplier>();
        supplier.Name.Returns("SkyGDS");
        supplier.PriceAsync("SkyGDS:a", default).ReturnsForAnyArgs(new PricedOffer(TestData.Offer(5000, "SkyGDS", "a")));

        var result = await new FlightPricingService([supplier], cache).PriceAsync("SkyGDS:a", 0m, CancellationToken.None);

        result.PriceChanged.Should().BeFalse();
    }

    [Fact]
    public async Task Unknown_or_expired_offer_throws_OfferUnavailable()
    {
        var cache = new FakeOfferCache();
        await cache.StoreAsync([TestData.Offer(5000, "SkyGDS", "old", expires: DateTimeOffset.UtcNow.AddMinutes(-1))], TimeSpan.FromMinutes(5));
        var sut = new FlightPricingService([TestData.Supplier("SkyGDS")], cache);

        await sut.Invoking(s => s.PriceAsync("SkyGDS:missing", 0, default)).Should().ThrowAsync<OfferUnavailableException>();
        await sut.Invoking(s => s.PriceAsync("SkyGDS:old", 0, default)).Should().ThrowAsync<OfferUnavailableException>();
    }
}
