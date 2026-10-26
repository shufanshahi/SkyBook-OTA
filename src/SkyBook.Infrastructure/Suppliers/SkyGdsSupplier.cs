using System.Globalization;
using System.Text;
using System.Xml.Linq;
using SkyBook.Application.Suppliers;

namespace SkyBook.Infrastructure.Suppliers;

/// <summary>SOAP 1.1 / XML client for the SkyGDS mock. Hand-rolled on purpose: build the envelope, post it, read faults.</summary>
public sealed class SkyGdsSupplier(IHttpClientFactory factory) : IFlightSupplier
{
    public const string ClientName = "SkyGds";
    public const string NoRetryClientName = "SkyGds.NoRetry";

    private static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    private static readonly XNamespace Gds = "http://skygds.mock/air/v1";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public string Name => "SkyGDS";

    // Search and price are safe to retry. Book/ticket/cancel use a client with retries disabled
    // so a timeout can never turn into a double booking or a double ticket.
    private HttpClient Safe => factory.CreateClient(ClientName);
    private HttpClient Unsafe => factory.CreateClient(NoRetryClientName);

    public async Task<IReadOnlyList<FlightOffer>> SearchAsync(FlightSearchRequest r, CancellationToken ct)
    {
        var rs = await CallAsync(Safe, "AirShopping", new XElement(Gds + "AirShoppingRQ",
            new XElement(Gds + "Origin", r.Origin),
            new XElement(Gds + "Destination", r.Destination),
            new XElement(Gds + "DepartureDate", r.DepartureDate.ToString("yyyy-MM-dd")),
            new XElement(Gds + "Adults", r.Adults),
            new XElement(Gds + "Cabin", r.Cabin)), ct);

        return rs.Descendants(Gds + "Itinerary").Select(MapItinerary).ToList();
    }

    public async Task<PricedOffer> PriceAsync(string offerId, CancellationToken ct)
    {
        var rs = await CallAsync(Safe, "AirPrice", new XElement(Gds + "AirPriceRQ",
            new XElement(Gds + "ItineraryId", OfferIds.Raw(Name, offerId))), ct);

        var it = rs.Descendants(Gds + "Itinerary").FirstOrDefault()
            ?? throw new SupplierException(Name, "AirPrice returned no itinerary");
        return new PricedOffer(MapItinerary(it));
    }

    public async Task<SupplierBookingResult> BookAsync(SupplierBookingRequest req, CancellationToken ct)
    {
        var rs = await CallAsync(Unsafe, "AirBook", new XElement(Gds + "AirBookRQ",
            new XElement(Gds + "ItineraryId", OfferIds.Raw(Name, req.OfferId)),
            new XElement(Gds + "Passengers", req.Passengers.Select(p => new XElement(Gds + "Passenger",
                new XElement(Gds + "Type", p.Type),
                new XElement(Gds + "Title", p.Title),
                new XElement(Gds + "FirstName", p.FirstName),
                new XElement(Gds + "LastName", p.LastName),
                new XElement(Gds + "DateOfBirth", p.DateOfBirth.ToString("yyyy-MM-dd")),
                new XElement(Gds + "PassportNo", p.PassportNo ?? ""))))), ct);

        var locator = Text(rs, "Locator");
        return new SupplierBookingResult(locator, locator, Dec(rs, "TotalFare"), Text(rs, "Currency"));
    }

    public async Task<SupplierTicketResult> IssueTicketAsync(string supplierRef, CancellationToken ct)
    {
        var rs = await CallAsync(Unsafe, "AirTicket",
            new XElement(Gds + "AirTicketRQ", new XElement(Gds + "Locator", supplierRef)), ct);
        var tickets = rs.Descendants(Gds + "TicketNumber").Select(t => t.Value).ToList();
        return new SupplierTicketResult(supplierRef, tickets);
    }

    public async Task<SupplierCancelResult> CancelAsync(string supplierRef, CancellationToken ct)
    {
        var rs = await CallAsync(Unsafe, "AirCancel",
            new XElement(Gds + "AirCancelRQ", new XElement(Gds + "Locator", supplierRef)), ct);
        return new SupplierCancelResult(supplierRef, Text(rs, "Status"));
    }

    // ---- SOAP plumbing ----------------------------------------------------------------

    private async Task<XElement> CallAsync(HttpClient http, string operation, XElement body, CancellationToken ct)
    {
        var envelope = new XDocument(
            new XElement(Soap + "Envelope",
                new XAttribute(XNamespace.Xmlns + "soap", Soap),
                new XAttribute(XNamespace.Xmlns + "gds", Gds),
                new XElement(Soap + "Body", body)));

        using var content = new StringContent(envelope.ToString(), Encoding.UTF8, "text/xml");
        content.Headers.Add("SOAPAction", $"\"{Gds.NamespaceName}/{operation}\"");

        HttpResponseMessage resp;
        try { resp = await http.PostAsync("/soap/air", content, ct); }
        catch (HttpRequestException ex) { throw new SupplierException(Name, $"connection failed ({ex.Message})", inner: ex); }

        using (resp)
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            XDocument xml;
            try { xml = XDocument.Parse(text); }
            catch (System.Xml.XmlException ex)
            {
                throw new SupplierException(Name, $"HTTP {(int)resp.StatusCode}, response is not XML", inner: ex);
            }

            // SOAP faults arrive as HTTP 500 with a <Fault> body: check the body, not just the status
            var fault = xml.Descendants(Soap + "Fault").FirstOrDefault();
            if (fault is not null)
                throw new SupplierException(Name, fault.Element("faultstring")?.Value ?? "Unknown SOAP fault",
                                            fault.Element("faultcode")?.Value);

            if (!resp.IsSuccessStatusCode)
                throw new SupplierException(Name, $"HTTP {(int)resp.StatusCode}");

            return xml.Descendants(Soap + "Body").First();
        }
    }

    private FlightOffer MapItinerary(XElement i)
    {
        string T(string n) => i.Element(Gds + n)?.Value ?? throw new SupplierException(Name, $"Itinerary is missing <{n}>");
        decimal D(string n) => decimal.Parse(T(n), Inv);

        var baseFare = D("BaseFare");
        var taxes = D("Taxes");
        var segment = new Segment(
            T("Carrier"), T("FlightNo"), T("Origin"), T("Destination"),
            DateTimeOffset.Parse(T("DepartureTime"), Inv, DateTimeStyles.AssumeUniversal),
            DateTimeOffset.Parse(T("ArrivalTime"), Inv, DateTimeStyles.AssumeUniversal));

        return new FlightOffer(
            OfferIds.Prefix(Name, T("ItineraryId")), Name, [segment],
            baseFare, taxes, baseFare + taxes,
            T("Currency"), T("Cabin"), int.Parse(T("SeatsAvailable"), Inv),
            T("Baggage"), bool.Parse(T("Refundable")),
            DateTimeOffset.Parse(T("ExpiresAt"), Inv, DateTimeStyles.AssumeUniversal));
    }

    private string Text(XElement e, string name) =>
        e.Descendants(Gds + name).FirstOrDefault()?.Value ?? throw new SupplierException(Name, $"Response is missing <{name}>");

    private decimal Dec(XElement e, string name) => decimal.Parse(Text(e, name), Inv);
}
