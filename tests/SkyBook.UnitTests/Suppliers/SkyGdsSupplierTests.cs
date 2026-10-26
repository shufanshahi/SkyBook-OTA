using System.Net;
using FluentAssertions;
using SkyBook.Application.Suppliers;
using SkyBook.Infrastructure.Suppliers;

namespace SkyBook.UnitTests.Suppliers;

public class SkyGdsSupplierTests
{
    private const string Itinerary = """
        <gds:Itinerary>
          <gds:ItineraryId>GDS-310d14bd-0</gds:ItineraryId><gds:Carrier>SB</gds:Carrier><gds:FlightNo>SB550</gds:FlightNo>
          <gds:Origin>DAC</gds:Origin><gds:Destination>CGP</gds:Destination>
          <gds:DepartureTime>2026-11-14T07:30:00.0000000Z</gds:DepartureTime><gds:ArrivalTime>2026-11-14T09:05:00.0000000Z</gds:ArrivalTime>
          <gds:BaseFare>3450.00</gds:BaseFare><gds:Taxes>414.00</gds:Taxes><gds:Currency>BDT</gds:Currency>
          <gds:Cabin>Economy</gds:Cabin><gds:SeatsAvailable>6</gds:SeatsAvailable><gds:Baggage>20kg</gds:Baggage>
          <gds:Refundable>false</gds:Refundable><gds:ExpiresAt>2026-11-14T00:15:00.0000000Z</gds:ExpiresAt>
        </gds:Itinerary>
        """;

    private static string Envelope(string body) =>
        $"""<soap:Envelope xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/" xmlns:gds="http://skygds.mock/air/v1"><soap:Body>{body}</soap:Body></soap:Envelope>""";

    private static (SkyGdsSupplier Sut, StubHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new SkyGdsSupplier(StubHandler.FactoryFor(handler)), handler);
    }

    [Fact]
    public async Task Search_maps_the_itinerary_to_a_normalized_offer()
    {
        var (sut, handler) = Create(_ => StubHandler.Xml(Envelope($"<gds:AirShoppingRS><gds:Itineraries>{Itinerary}</gds:Itineraries></gds:AirShoppingRS>")));

        var offers = await sut.SearchAsync(TestData.DacToCgp(), CancellationToken.None);

        var o = offers.Should().ContainSingle().Subject;
        o.OfferId.Should().Be("SkyGDS:GDS-310d14bd-0");
        o.Supplier.Should().Be("SkyGDS");
        (o.BaseFare, o.Taxes, o.Total).Should().Be((3450m, 414m, 3864m));
        o.Segments.Single().FlightNo.Should().Be("SB550");
        o.Segments.Single().DepartureUtc.Should().Be(new DateTimeOffset(2026, 11, 14, 7, 30, 0, TimeSpan.Zero));
        o.Refundable.Should().BeFalse();
        o.SeatsAvailable.Should().Be(6);

        var (request, body) = handler.Seen.Single();
        request.RequestUri!.AbsolutePath.Should().Be("/soap/air");
        request.Content!.Headers.GetValues("SOAPAction").Single().Should().Be("\"http://skygds.mock/air/v1/AirShopping\"");
        body.Should().Contain("<gds:Origin>DAC</gds:Origin>").And.Contain("<gds:DepartureDate>2026-11-14</gds:DepartureDate>");
    }

    [Fact]
    public async Task Soap_fault_becomes_SupplierException_even_with_http_500()
    {
        var (sut, _) = Create(_ => StubHandler.Xml(Envelope("<soap:Fault><faultcode>soap:Server</faultcode><faultstring>SkyGDS internal error</faultstring></soap:Fault>"), HttpStatusCode.InternalServerError));

        var act = () => sut.SearchAsync(TestData.DacToCgp(), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<SupplierException>()).Which;
        ex.Supplier.Should().Be("SkyGDS");
        ex.Message.Should().Contain("SkyGDS internal error");
        ex.Code.Should().Be("soap:Server");
    }

    [Fact]
    public async Task Non_xml_response_becomes_SupplierException()
    {
        var (sut, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = new StringContent("<html>nginx</html") });

        var act = () => sut.SearchAsync(TestData.DacToCgp(), CancellationToken.None);

        (await act.Should().ThrowAsync<SupplierException>()).Which.Message.Should().Contain("502");
    }

    [Fact]
    public async Task Price_strips_the_supplier_prefix_and_maps_the_new_fare()
    {
        var (sut, handler) = Create(_ => StubHandler.Xml(Envelope($"<gds:AirPriceRS><gds:Itineraries>{Itinerary}</gds:Itineraries></gds:AirPriceRS>")));

        var priced = await sut.PriceAsync("SkyGDS:GDS-310d14bd-0", CancellationToken.None);

        priced.Offer.Total.Should().Be(3864m);
        handler.Seen.Single().Body.Should().Contain("<gds:ItineraryId>GDS-310d14bd-0</gds:ItineraryId>");
    }

    [Fact]
    public async Task Book_sends_passengers_and_returns_the_locator()
    {
        var (sut, handler) = Create(_ => StubHandler.Xml(Envelope("<gds:AirBookRS><gds:Locator>SFTXMD</gds:Locator><gds:Status>HK</gds:Status><gds:TotalFare>4200.00</gds:TotalFare><gds:Currency>BDT</gds:Currency></gds:AirBookRS>")));
        var pax = new SupplierPassenger("ADT", "Ms", "Ayesha", "Rahman", new DateOnly(1990, 4, 2), "A1234567");

        var result = await sut.BookAsync(new SupplierBookingRequest("SkyGDS:GDS-1-0", [pax]), CancellationToken.None);

        result.Should().Be(new SupplierBookingResult("SFTXMD", "SFTXMD", 4200m, "BDT"));
        handler.Seen.Single().Body.Should().Contain("<gds:FirstName>Ayesha</gds:FirstName>").And.Contain("<gds:ItineraryId>GDS-1-0</gds:ItineraryId>");
    }

    [Fact]
    public async Task Ticket_returns_ticket_numbers_and_already_ticketed_fault_is_surfaced()
    {
        var (ok, _) = Create(_ => StubHandler.Xml(Envelope("<gds:AirTicketRS><gds:Locator>ABC</gds:Locator><gds:Status>TK</gds:Status><gds:Tickets><gds:TicketNumber>9980001</gds:TicketNumber></gds:Tickets></gds:AirTicketRS>")));
        (await ok.IssueTicketAsync("ABC", CancellationToken.None)).TicketNumbers.Should().Equal("9980001");

        var (dup, _) = Create(_ => StubHandler.Xml(Envelope("<soap:Fault><faultcode>soap:Client</faultcode><faultstring>Already ticketed: ABC</faultstring></soap:Fault>"), HttpStatusCode.InternalServerError));
        var act = () => dup.IssueTicketAsync("ABC", CancellationToken.None);
        (await act.Should().ThrowAsync<SupplierException>()).Which.Message.Should().Contain("Already ticketed");
    }
}
