using System.Net;
using System.Text;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using SkyBook.Application.Suppliers;

namespace SkyBook.UnitTests.Suppliers;

public static class TestData
{
    public static FlightSearchRequest DacToCgp() => new("DAC", "CGP", new DateOnly(2026, 11, 14));

    public static FlightOffer Offer(decimal total = 5000, string supplier = "AeroNDC", string id = "1",
                                    DateTimeOffset? expires = null) =>
        new($"{supplier}:{id}", supplier,
            [new Segment("AN", "AN100", "DAC", "CGP", DateTimeOffset.UtcNow.AddDays(5), DateTimeOffset.UtcNow.AddDays(5).AddHours(1))],
            total - 600, 600, total, "BDT", "Economy", 5, "20kg", true,
            expires ?? DateTimeOffset.UtcNow.AddMinutes(15));

    public static IFlightSupplier Supplier(string name, params FlightOffer[] offers)
    {
        var s = Substitute.For<IFlightSupplier>();
        s.Name.Returns(name);
        s.SearchAsync(default!, default).ReturnsForAnyArgs(offers);
        return s;
    }

    public static IFlightSupplier FailingSupplier(string name, Exception ex)
    {
        var s = Substitute.For<IFlightSupplier>();
        s.Name.Returns(name);
        s.SearchAsync(default!, default).ThrowsAsyncForAnyArgs(ex);
        return s;
    }
}

public sealed class FakeOfferCache : IOfferCache
{
    public Dictionary<string, FlightOffer> Items { get; } = [];

    public Task StoreAsync(IEnumerable<FlightOffer> offers, TimeSpan ttl)
    {
        foreach (var o in offers) Items[o.OfferId] = o;
        return Task.CompletedTask;
    }

    public Task<FlightOffer?> GetAsync(string offerId) => Task.FromResult(Items.GetValueOrDefault(offerId));
}

/// <summary>An HttpMessageHandler that returns a canned response and records the requests it saw.</summary>
public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string Body)> Seen { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Seen.Add((request, request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct)));
        return respond(request);
    }

    public static HttpResponseMessage Xml(string xml, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(xml, Encoding.UTF8, "text/xml") };

    public static HttpResponseMessage Json(string json, HttpStatusCode code = HttpStatusCode.OK) =>
        new(code) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static IHttpClientFactory FactoryFor(StubHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri("http://stub")
        });
        return factory;
    }
}
