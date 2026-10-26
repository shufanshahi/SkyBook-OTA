using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkyBook.Application.Suppliers;

namespace SkyBook.UnitTests.Suppliers;

public class FlightSearchServiceTests
{
    private static FlightSearchService Sut(FakeOfferCache cache, TimeSpan? timeout = null, params IFlightSupplier[] suppliers) =>
        new(suppliers, cache, NullLogger<FlightSearchService>.Instance, timeout);

    [Fact]
    public async Task Search_merges_both_suppliers_sorted_by_price()
    {
        var sut = Sut(new FakeOfferCache(), null,
            TestData.Supplier("SkyGDS", TestData.Offer(6000, "SkyGDS", "a"), TestData.Offer(4000, "SkyGDS", "b")),
            TestData.Supplier("AeroNDC", TestData.Offer(5000, "AeroNDC", "c")));

        var result = await sut.SearchAsync(TestData.DacToCgp(), CancellationToken.None);

        result.Offers.Select(o => o.Total).Should().Equal(4000, 5000, 6000);
        result.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_returns_partial_results_when_one_supplier_fails()
    {
        var sut = Sut(new FakeOfferCache(), null,
            TestData.Supplier("AeroNDC", TestData.Offer(5200)),
            TestData.FailingSupplier("SkyGDS", new SupplierException("SkyGDS", "boom")));

        var result = await sut.SearchAsync(TestData.DacToCgp(), CancellationToken.None);

        result.Offers.Should().HaveCount(1);
        result.Warnings.Should().ContainSingle(w => w.Supplier == "SkyGDS" && w.Message.Contains("boom"));
    }

    [Fact]
    public async Task Search_returns_no_offers_and_two_warnings_when_both_fail()
    {
        var sut = Sut(new FakeOfferCache(), null,
            TestData.FailingSupplier("SkyGDS", new SupplierException("SkyGDS", "down")),
            TestData.FailingSupplier("AeroNDC", new SupplierException("AeroNDC", "down")));

        var result = await sut.SearchAsync(TestData.DacToCgp(), CancellationToken.None);

        result.Offers.Should().BeEmpty();
        result.Warnings.Should().HaveCount(2);
    }

    [Fact]
    public async Task Search_applies_agent_markup_but_caches_supplier_price()
    {
        var cache = new FakeOfferCache();
        var sut = Sut(cache, null, TestData.Supplier("AeroNDC", TestData.Offer(5000, "AeroNDC", "x")));

        var result = await sut.SearchAsync(TestData.DacToCgp(), markupPercent: 10m, CancellationToken.None);

        result.Offers.Single().Total.Should().Be(5500m);
        cache.Items["AeroNDC:x"].Total.Should().Be(5000m);
    }

    [Fact]
    public async Task Slow_supplier_times_out_into_a_warning_without_blocking_the_other()
    {
        var slow = Substitute.For<IFlightSupplier>();
        slow.Name.Returns("SkyGDS");
        slow.SearchAsync(default!, default).ReturnsForAnyArgs(async ci =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ci.Arg<CancellationToken>());
            return (IReadOnlyList<FlightOffer>)[];
        });
        var sut = Sut(new FakeOfferCache(), TimeSpan.FromMilliseconds(200), slow, TestData.Supplier("AeroNDC", TestData.Offer()));

        var started = DateTime.UtcNow;
        var result = await sut.SearchAsync(TestData.DacToCgp(), CancellationToken.None);

        (DateTime.UtcNow - started).Should().BeLessThan(TimeSpan.FromSeconds(5));
        result.Offers.Should().HaveCount(1);
        result.Warnings.Should().ContainSingle(w => w.Supplier == "SkyGDS" && w.Message == "timed out");
    }

    [Fact]
    public async Task Caller_cancellation_is_not_swallowed()
    {
        var sut = Sut(new FakeOfferCache(), null, TestData.FailingSupplier("SkyGDS", new OperationCanceledException()));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => sut.SearchAsync(TestData.DacToCgp(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
