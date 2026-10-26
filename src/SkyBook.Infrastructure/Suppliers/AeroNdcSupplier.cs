using System.Net.Http.Json;
using System.Text.Json;
using SkyBook.Application.Suppliers;

namespace SkyBook.Infrastructure.Suppliers;

/// <summary>REST/JSON client for the AeroNDC mock (the Go service).</summary>
public sealed class AeroNdcSupplier(IHttpClientFactory factory) : IFlightSupplier
{
    public const string ClientName = "AeroNdc";
    public const string NoRetryClientName = "AeroNdc.NoRetry";

    public string Name => "AeroNDC";

    private HttpClient Safe => factory.CreateClient(ClientName);
    private HttpClient Unsafe => factory.CreateClient(NoRetryClientName);

    public async Task<IReadOnlyList<FlightOffer>> SearchAsync(FlightSearchRequest r, CancellationToken ct)
    {
        var body = await PostAsync<SearchResponse>(Safe, "/v1/offers/search", new
        {
            origin = r.Origin, destination = r.Destination,
            date = r.DepartureDate.ToString("yyyy-MM-dd"),
            pax = new { adt = r.Adults, chd = r.Children, inf = r.Infants },
            cabin = r.Cabin
        }, ct);
        return body.Offers.Select(Map).ToList();
    }

    public async Task<PricedOffer> PriceAsync(string offerId, CancellationToken ct)
    {
        var body = await PostAsync<PriceResponse>(Safe, $"/v1/offers/{OfferIds.Raw(Name, offerId)}/price", new { }, ct);
        return new PricedOffer(Map(body.Offer));
    }

    public async Task<SupplierBookingResult> BookAsync(SupplierBookingRequest req, CancellationToken ct)
    {
        var o = await PostAsync<OrderDto>(Unsafe, "/v1/orders", new
        {
            offerId = OfferIds.Raw(Name, req.OfferId),
            passengers = req.Passengers.Select(p => new
            {
                type = p.Type, title = p.Title, firstName = p.FirstName, lastName = p.LastName,
                dateOfBirth = p.DateOfBirth.ToString("yyyy-MM-dd"), passportNo = p.PassportNo
            })
        }, ct);
        return new SupplierBookingResult(o.OrderId, o.Pnr, o.Total, o.Currency);
    }

    public async Task<SupplierTicketResult> IssueTicketAsync(string supplierRef, CancellationToken ct)
    {
        var o = await PostAsync<OrderDto>(Unsafe, $"/v1/orders/{supplierRef}/ticket", new { }, ct);
        return new SupplierTicketResult(o.OrderId, o.TicketNumbers ?? []);
    }

    public async Task<SupplierCancelResult> CancelAsync(string supplierRef, CancellationToken ct)
    {
        var o = await PostAsync<OrderDto>(Unsafe, $"/v1/orders/{supplierRef}/cancel", new { }, ct);
        return new SupplierCancelResult(o.OrderId, o.Status);
    }

    // ---- plumbing ---------------------------------------------------------------------

    private async Task<T> PostAsync<T>(HttpClient http, string path, object payload, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await http.PostAsJsonAsync(path, payload, ct); }
        catch (HttpRequestException ex) { throw new SupplierException(Name, $"connection failed ({ex.Message})", inner: ex); }

        using (resp)
        {
            if (!resp.IsSuccessStatusCode)
                throw await ToExceptionAsync(resp, ct);

            try
            {
                return await resp.Content.ReadFromJsonAsync<T>(ct)
                    ?? throw new SupplierException(Name, "empty response body");
            }
            catch (JsonException ex) { throw new SupplierException(Name, "response is not valid JSON", inner: ex); }
        }
    }

    // NDC errors look like {"errors":[{"code":"OFFER_EXPIRED","message":"..."}]}
    private async Task<SupplierException> ToExceptionAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<ErrorResponse>(ct);
            var first = err?.Errors?.FirstOrDefault();
            if (first is not null)
                return new SupplierException(Name, first.Message ?? first.Code ?? $"HTTP {(int)resp.StatusCode}", first.Code);
        }
        catch (JsonException) { /* fall through to the generic message */ }
        return new SupplierException(Name, $"HTTP {(int)resp.StatusCode}");
    }

    private FlightOffer Map(OfferDto o) => new(
        OfferIds.Prefix(Name, o.OfferId), Name,
        [new Segment(o.Carrier, o.FlightNo, o.Origin, o.Destination, o.Departure, o.Arrival)],
        o.BaseFare, o.Taxes, o.BaseFare + o.Taxes,
        o.Currency, o.Cabin, o.Seats, o.Baggage, o.Refundable, o.ExpiresAt);

    // wire DTOs (camelCase JSON, matched case-insensitively)
    private record SearchResponse(List<OfferDto> Offers);
    private record PriceResponse(OfferDto Offer);
    private record OfferDto(string OfferId, string Carrier, string FlightNo, string Origin, string Destination,
        DateTimeOffset Departure, DateTimeOffset Arrival, decimal BaseFare, decimal Taxes, string Currency,
        string Cabin, int Seats, string Baggage, bool Refundable, DateTimeOffset ExpiresAt);
    private record OrderDto(string OrderId, string Pnr, string Status, decimal Total, string Currency, List<string>? TicketNumbers);
    private record ErrorResponse(List<ErrorItem>? Errors);
    private record ErrorItem(string? Code, string? Message);
}
