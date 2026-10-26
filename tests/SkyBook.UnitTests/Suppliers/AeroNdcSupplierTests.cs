using System.Net;
using FluentAssertions;
using SkyBook.Application.Suppliers;
using SkyBook.Infrastructure.Suppliers;

namespace SkyBook.UnitTests.Suppliers;

public class AeroNdcSupplierTests
{
    private const string OfferJson = """
        {"offerId":"NDC-2a635ff4-0","carrier":"AN","flightNo":"AN498","origin":"DAC","destination":"CGP",
         "departure":"2026-11-14T06:30:00Z","arrival":"2026-11-14T07:25:00Z","baseFare":5150,"taxes":618,
         "currency":"BDT","cabin":"Economy","seats":4,"baggage":"20kg","refundable":true,"expiresAt":"2026-11-14T00:15:00Z"}
        """;

    private static (AeroNdcSupplier Sut, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new AeroNdcSupplier(StubHandler.FactoryFor(handler)), handler);
    }

    [Fact]
    public async Task Search_maps_json_offers_and_posts_the_expected_body()
    {
        var (sut, handler) = Create(_ => StubHandler.Json($$"""{"offers":[{{OfferJson}}]}"""));

        var offers = await sut.SearchAsync(TestData.DacToCgp() with { Adults = 2, Children = 1 }, CancellationToken.None);

        var o = offers.Should().ContainSingle().Subject;
        o.OfferId.Should().Be("AeroNDC:NDC-2a635ff4-0");
        o.Total.Should().Be(5768m);
        o.Refundable.Should().BeTrue();
        o.Segments.Single().Carrier.Should().Be("AN");

        var (request, body) = handler.Seen.Single();
        request.RequestUri!.AbsolutePath.Should().Be("/v1/offers/search");
        body.Should().Contain("\"origin\":\"DAC\"").And.Contain("\"adt\":2").And.Contain("\"chd\":1");
    }

    [Fact]
    public async Task Price_calls_the_offer_endpoint_without_the_supplier_prefix()
    {
        var (sut, handler) = Create(_ => StubHandler.Json($$"""{"offer":{{OfferJson}}}"""));

        var priced = await sut.PriceAsync("AeroNDC:NDC-2a635ff4-0", CancellationToken.None);

        priced.Offer.Total.Should().Be(5768m);
        handler.Seen.Single().Request.RequestUri!.AbsolutePath.Should().Be("/v1/offers/NDC-2a635ff4-0/price");
    }

    [Fact]
    public async Task Expired_offer_error_body_is_mapped_with_its_code()
    {
        var (sut, _) = Create(_ => StubHandler.Json("""{"errors":[{"code":"OFFER_EXPIRED","message":"offer has expired, search again"}]}""", HttpStatusCode.Conflict));

        var act = () => sut.PriceAsync("AeroNDC:NDC-x", CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<SupplierException>()).Which;
        ex.Code.Should().Be("OFFER_EXPIRED");
        ex.Message.Should().Contain("expired");
    }

    [Fact]
    public async Task Http_error_without_ndc_body_falls_back_to_status_code()
    {
        var (sut, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("oops") });

        var act = () => sut.SearchAsync(TestData.DacToCgp(), CancellationToken.None);

        (await act.Should().ThrowAsync<SupplierException>()).Which.Message.Should().Contain("503");
    }

    [Fact]
    public async Task Book_and_ticket_round_trip()
    {
        var (sut, handler) = Create(r => r.RequestUri!.AbsolutePath.EndsWith("/ticket")
            ? StubHandler.Json("""{"orderId":"ORD-1001","pnr":"AAMMXU","status":"TICKETED","total":5768,"currency":"BDT","ticketNumbers":["9980572392084"]}""")
            : StubHandler.Json("""{"orderId":"ORD-1001","pnr":"AAMMXU","status":"HELD","total":5768,"currency":"BDT"}""", HttpStatusCode.Created));
        var pax = new SupplierPassenger("ADT", "Mr", "Karim", "Uddin", new DateOnly(1985, 1, 1), null);

        var booked = await sut.BookAsync(new SupplierBookingRequest("AeroNDC:NDC-2a635ff4-0", [pax]), CancellationToken.None);
        var ticketed = await sut.IssueTicketAsync(booked.SupplierRef, CancellationToken.None);

        booked.Should().Be(new SupplierBookingResult("ORD-1001", "AAMMXU", 5768m, "BDT"));
        ticketed.TicketNumbers.Should().Equal("9980572392084");
        handler.Seen[0].Body.Should().Contain("\"offerId\":\"NDC-2a635ff4-0\"");
    }
}
