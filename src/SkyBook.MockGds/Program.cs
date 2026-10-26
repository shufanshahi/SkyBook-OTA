using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

// "SkyGDS": a fake GDS speaking SOAP 1.1 over HTTP. One endpoint (POST /soap/air);
// the operation is chosen by the SOAPAction header.
// Test triggers: Origin=ERR -> SOAP fault, Destination=SLW -> 8s delay,
// AirPrice -> 50% chance of a higher price, AirTicket twice -> "Already ticketed" fault.

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

XNamespace soap = "http://schemas.xmlsoap.org/soap/envelope/";
XNamespace gds = "http://skygds.mock/air/v1";

var itineraries = new ConcurrentDictionary<string, Itinerary>();
var bookings = new ConcurrentDictionary<string, BookingState>();
var domestic = new HashSet<string> { "DAC", "CGP", "CXB", "ZYL", "JSR", "SPD", "BZL" };
var bookingSeq = 0;

app.MapGet("/health", () => "ok");

app.MapPost("/soap/air", async (HttpRequest request) =>
{
    var action = request.Headers["SOAPAction"].ToString().Trim('"');
    var operation = action[(action.LastIndexOf('/') + 1)..];

    XDocument doc;
    try { doc = await XDocument.LoadAsync(request.Body, LoadOptions.None, request.HttpContext.RequestAborted); }
    catch (Exception) { return Fault("soap:Client", "Request body is not valid XML"); }

    var body = doc.Descendants(soap + "Body").FirstOrDefault()?.Elements().FirstOrDefault();
    if (body is null) return Fault("soap:Client", "Missing SOAP Body");

    string? Get(string name) => body.Element(gds + name)?.Value;

    switch (operation)
    {
        case "AirShopping":
        {
            var origin = Get("Origin") ?? "";
            var destination = Get("Destination") ?? "";
            var date = Get("DepartureDate") ?? "";
            var cabin = Get("Cabin") ?? "Economy";

            if (origin == "ERR") return Fault("soap:Server", "SkyGDS internal error (simulated)");
            if (destination == "SLW") await Task.Delay(TimeSpan.FromSeconds(8));
            if (!DateTime.TryParse(date, out var day)) return Fault("soap:Client", "DepartureDate must be yyyy-MM-dd");

            var list = Generate(origin, destination, day.Date, cabin);
            foreach (var i in list) itineraries[i.Id] = i;

            return Soap(new XElement(gds + "AirShoppingRS",
                new XElement(gds + "Itineraries", list.Select(ToXml))));
        }

        case "AirPrice":
        {
            var id = Get("ItineraryId") ?? "";
            if (!itineraries.TryGetValue(id, out var it)) return Fault("soap:Client", $"Unknown itinerary {id}");
            if (DateTime.UtcNow > it.ExpiresAt) return Fault("soap:Client", $"Itinerary {id} has expired");

            // 50% of the time the fare has moved up 3%: lets you test price-change handling.
            if (Random.Shared.Next(2) == 0)
            {
                var newBase = Math.Round(it.BaseFare * 1.03m / 50) * 50;
                it = it with { BaseFare = newBase, Taxes = Math.Round(newBase * 0.12m) };
                itineraries[id] = it;
            }
            return Soap(new XElement(gds + "AirPriceRS", new XElement(gds + "Itineraries", ToXml(it))));
        }

        case "AirBook":
        {
            var id = Get("ItineraryId") ?? "";
            if (!itineraries.TryGetValue(id, out var it)) return Fault("soap:Client", $"Unknown itinerary {id}");
            if (DateTime.UtcNow > it.ExpiresAt) return Fault("soap:Client", $"Itinerary {id} has expired");

            var paxCount = Math.Max(1, body.Descendants(gds + "Passenger").Count());
            var locator = Locator(Interlocked.Increment(ref bookingSeq));
            bookings[locator] = new BookingState(locator, id, paxCount, "HK", []);

            return Soap(new XElement(gds + "AirBookRS",
                new XElement(gds + "Locator", locator),
                new XElement(gds + "Status", "HK"),
                new XElement(gds + "TotalFare", ((it.BaseFare + it.Taxes) * paxCount).ToString("0.00")),
                new XElement(gds + "Currency", "BDT")));
        }

        case "AirTicket":
        {
            var locator = Get("Locator") ?? "";
            if (!bookings.TryGetValue(locator, out var b)) return Fault("soap:Client", $"Unknown locator {locator}");
            if (b.Status == "XX") return Fault("soap:Client", $"Booking {locator} is cancelled");
            if (b.Status == "TK") return Fault("soap:Client", $"Already ticketed: {locator}");

            var tickets = Enumerable.Range(0, b.Passengers)
                .Select(n => "998" + Math.Abs(HashCode.Combine(locator, n)).ToString("D10")).ToList();
            bookings[locator] = b with { Status = "TK", Tickets = tickets };

            return Soap(new XElement(gds + "AirTicketRS",
                new XElement(gds + "Locator", locator),
                new XElement(gds + "Status", "TK"),
                new XElement(gds + "Tickets", tickets.Select(t => new XElement(gds + "TicketNumber", t)))));
        }

        case "AirCancel":
        {
            var locator = Get("Locator") ?? "";
            if (!bookings.TryGetValue(locator, out var b)) return Fault("soap:Client", $"Unknown locator {locator}");
            if (b.Status == "XX") return Fault("soap:Client", $"Booking {locator} is already cancelled");

            bookings[locator] = b with { Status = "XX" };
            return Soap(new XElement(gds + "AirCancelRS",
                new XElement(gds + "Locator", locator),
                new XElement(gds + "Status", "XX")));
        }

        default:
            return Fault("soap:Client", $"Unknown SOAPAction '{action}'");
    }
});

app.Run();

// ---- helpers ------------------------------------------------------------------------

IResult Soap(XElement content)
{
    var envelope = new XDocument(new XElement(soap + "Envelope",
        new XAttribute(XNamespace.Xmlns + "soap", soap),
        new XAttribute(XNamespace.Xmlns + "gds", gds),
        new XElement(soap + "Body", content)));
    return Results.Content(envelope.ToString(), "text/xml", Encoding.UTF8);
}

// SOAP 1.1 faults travel with HTTP 500.
IResult Fault(string code, string message)
{
    var envelope = new XDocument(new XElement(soap + "Envelope",
        new XAttribute(XNamespace.Xmlns + "soap", soap),
        new XElement(soap + "Body",
            new XElement(soap + "Fault",
                new XElement("faultcode", code),
                new XElement("faultstring", message)))));
    return Results.Content(envelope.ToString(), "text/xml", Encoding.UTF8, statusCode: 500);
}

XElement ToXml(Itinerary i) => new(gds + "Itinerary",
    new XElement(gds + "ItineraryId", i.Id),
    new XElement(gds + "Carrier", i.Carrier),
    new XElement(gds + "FlightNo", i.FlightNo),
    new XElement(gds + "Origin", i.Origin),
    new XElement(gds + "Destination", i.Destination),
    new XElement(gds + "DepartureTime", i.Departure.ToString("o")),
    new XElement(gds + "ArrivalTime", i.Arrival.ToString("o")),
    new XElement(gds + "BaseFare", i.BaseFare.ToString("0.00")),
    new XElement(gds + "Taxes", i.Taxes.ToString("0.00")),
    new XElement(gds + "Currency", "BDT"),
    new XElement(gds + "Cabin", i.Cabin),
    new XElement(gds + "SeatsAvailable", i.Seats),
    new XElement(gds + "Baggage", i.Baggage),
    new XElement(gds + "Refundable", i.Refundable ? "true" : "false"),
    new XElement(gds + "ExpiresAt", i.ExpiresAt.ToString("o")));

// Deterministic per route/date/cabin so tests and demos are repeatable.
List<Itinerary> Generate(string origin, string destination, DateTime day, string cabin)
{
    var intl = !domestic.Contains(origin) || !domestic.Contains(destination);
    var seed = Hash($"{origin}|{destination}|{day:yyyy-MM-dd}|{cabin}|gds");
    var factor = cabin == "Business" ? 2.4m : 1m;
    var result = new List<Itinerary>();
    for (var i = 0; i < 3 + (int)(seed % 3); i++)
    {
        var s = Hash($"{seed}|{i}");
        decimal baseFare = intl ? 12000 + s % 28000 : 3000 + s % 4000;
        baseFare = Math.Floor(baseFare * factor / 50) * 50;
        var duration = intl ? TimeSpan.FromMinutes(120 + s % 240) : TimeSpan.FromMinutes(45 + s % 60);
        var dep = DateTime.SpecifyKind(day, DateTimeKind.Utc).AddHours(7 + i * 3).AddMinutes((s % 4) * 15);
        result.Add(new Itinerary(
            $"GDS-{seed:x8}-{i}", "SB", $"SB{100 + (int)(s % 500)}", origin, destination,
            dep, dep + duration, baseFare, Math.Floor(baseFare * 0.12m), cabin,
            1 + (int)(s % 9), intl ? "30kg" : "20kg", s % 2 == 1,
            destination == "EXP" ? DateTime.UtcNow.AddMinutes(-1) : DateTime.UtcNow.AddMinutes(15)));
    }
    return result;
}

static uint Hash(string s) => BitConverter.ToUInt32(SHA256.HashData(Encoding.UTF8.GetBytes(s)), 0);

static string Locator(int seq)
{
    const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    var h = Hash($"loc-{seq}");
    var chars = new char[6];
    for (var i = 0; i < 6; i++) { chars[i] = letters[(int)(h % (uint)letters.Length)]; h = h / (uint)letters.Length + (uint)(seq * 7919 + i); }
    return new string(chars);
}

record Itinerary(string Id, string Carrier, string FlightNo, string Origin, string Destination,
                 DateTime Departure, DateTime Arrival, decimal BaseFare, decimal Taxes, string Cabin,
                 int Seats, string Baggage, bool Refundable, DateTime ExpiresAt);

record BookingState(string Locator, string ItineraryId, int Passengers, string Status, List<string> Tickets);
