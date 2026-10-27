using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SkyBook.Domain.Enums;

namespace SkyBook.IntegrationTests;

public sealed class WebFlowTests(SkyBookFactory app) : IClassFixture<SkyBookFactory>
{
    private const string OfferId = "FakeAir:DAC-CGP";
    private static readonly string Create = $"/Bookings/Create?offerId={Uri.EscapeDataString(OfferId)}&adults=1";

    // ---- helpers ------------------------------------------------------------------------

    private async Task<HttpClient> LoggedInAgentAsync(decimal wallet, decimal credit = 0)
    {
        var http = app.NewBrowser();
        await http.LoginAsync(await app.CreateAgentAsync(wallet, credit), "Agent@123");
        return http;
    }

    /// <summary>Opens the review page for the fake offer and returns the form fields a browser would post.</summary>
    private async Task<(string Token, Dictionary<string, string> Fields)> OpenBookingFormAsync(HttpClient http)
    {
        // the offer must be in the cache, exactly like after a real search
        (await http.GetAsync("/Flights/Search?Origin=DAC&Destination=CGP&DepartureDate=" +
            DateTime.Today.AddDays(14).ToString("yyyy-MM-dd") + "&Adults=1&Children=0&Infants=0&Cabin=Economy")).StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await (await http.GetAsync(Create)).BodyAsync();
        var fields = HttpExtensions.HiddenFields(page);
        fields["Passengers[0].Title"] = "Ms";
        fields["Passengers[0].FirstName"] = "Ayesha";
        fields["Passengers[0].LastName"] = "Rahman";
        fields["Passengers[0].DateOfBirth"] = "1990-04-02";
        fields["Passengers[0].PassportNo"] = "";
        return (HttpExtensions.Token(page), fields);
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string url, string token, Dictionary<string, string> fields) =>
        http.PostAsync(url, new FormUrlEncodedContent(new Dictionary<string, string>(fields) { ["__RequestVerificationToken"] = token }));

    private static string PnrFrom(HttpResponseMessage r)
    {
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.OriginalString.Should().Contain("/Bookings/Details/");
        return r.Headers.Location!.OriginalString.Split('/').Last();
    }

    private async Task<string> BookAsync(HttpClient http)
    {
        var (token, fields) = await OpenBookingFormAsync(http);
        return PnrFrom(await PostAsync(http, "/Bookings/Create", token, fields));
    }

    private Task<decimal> WalletAsync(string pnr) => app.QueryAsync(async db =>
        await db.Bookings.Where(b => b.Pnr == pnr).Select(b => b.Agent.WalletBalance).SingleAsync());

    // ---- authentication / authorization ---------------------------------------------------

    [SqlFact]
    public async Task Anonymous_request_is_redirected_to_login()
    {
        var r = await app.NewBrowser().GetAsync("/Bookings");
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.OriginalString.Should().Contain("/Account/Login");
    }

    [SqlFact]
    public async Task Wrong_password_stays_on_login_page()
    {
        var http = app.NewBrowser();
        var page = await (await http.GetAsync("/Account/Login")).BodyAsync();
        var r = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "agent1@skybook.test", ["Password"] = "nope", ["__RequestVerificationToken"] = HttpExtensions.Token(page)
        }));
        r.StatusCode.Should().Be(HttpStatusCode.OK);
        (await r.BodyAsync()).Should().Contain("Invalid email or password");
    }

    [SqlFact]
    public async Task Post_without_anti_forgery_token_is_rejected()
    {
        var http = await LoggedInAgentAsync(10_000);
        var r = await http.PostAsync("/Bookings/Cancel/ABCDEF", new FormUrlEncodedContent(new Dictionary<string, string>()));
        r.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [SqlFact]
    public async Task Agent_cannot_open_admin_area()
    {
        var r = await (await LoggedInAgentAsync(1000)).GetAsync("/Admin/Agents");
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);
        r.Headers.Location!.OriginalString.Should().Contain("AccessDenied");
    }

    [SqlFact]
    public async Task Admin_can_open_admin_pages()
    {
        var http = app.NewBrowser();
        await http.LoginAsync("admin@skybook.test", "Admin@123");
        foreach (var path in new[] { "/Admin/Agents", "/Admin/Bookings", "/Admin/Refunds", "/Admin/Reports/Sales", "/Admin/SupplierLogs" })
            (await http.GetAsync(path)).StatusCode.Should().Be(HttpStatusCode.OK, path);
    }

    [SqlFact]
    public async Task Login_ignores_external_return_url()
    {
        var http = app.NewBrowser();
        var email = await app.CreateAgentAsync(100);
        var page = await (await http.GetAsync("/Account/Login")).BodyAsync();
        var r = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = "Agent@123", ["ReturnUrl"] = "https://evil.example/", ["__RequestVerificationToken"] = HttpExtensions.Token(page)
        }));
        r.Headers.Location!.OriginalString.Should().NotContain("evil.example");
    }

    // ---- search ---------------------------------------------------------------------------

    [SqlFact]
    public async Task Search_shows_offers_with_agent_markup_applied()
    {
        var http = app.NewBrowser();
        await http.LoginAsync(await app.CreateAgentAsync(1000, markup: 10), "Agent@123");
        var html = await (await http.GetAsync("/Flights/Search?Origin=DAC&Destination=CGP&DepartureDate=" +
            DateTime.Today.AddDays(10).ToString("yyyy-MM-dd") + "&Adults=1&Children=0&Infants=0&Cabin=Economy")).BodyAsync();
        html.Should().Contain("FakeAir").And.Contain("2,200");   // 2000 + 10%
    }

    // ---- booking lifecycle ----------------------------------------------------------------

    [SqlFact]
    public async Task Full_flow_book_ticket_cancel_refund_moves_the_wallet()
    {
        var email = await app.CreateAgentAsync(10_000);
        var http = app.NewBrowser();
        await http.LoginAsync(email, "Agent@123");

        var pnr = await BookAsync(http);
        (await WalletAsync(pnr)).Should().Be(10_000, "holding a booking does not cost anything");

        var details = $"/Bookings/Details/{pnr}";
        (await http.PostFormAsync($"/Bookings/Ticket/{pnr}", details)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await WalletAsync(pnr)).Should().Be(10_000 - FakeSupplier.Fare);
        (await app.QueryAsync(db => db.Bookings.Where(b => b.Pnr == pnr).Select(b => b.Status).SingleAsync())).Should().Be(BookingStatus.Ticketed);
        (await app.QueryAsync(db => db.Tickets.CountAsync(t => db.Bookings.Any(b => b.Id == t.BookingId && b.Pnr == pnr)))).Should().Be(1);

        (await http.PostFormAsync($"/Bookings/Cancel/{pnr}", details)).StatusCode.Should().Be(HttpStatusCode.Redirect);
        var refund = await app.QueryAsync(db => db.Refunds.SingleAsync(r => db.Bookings.Any(b => b.Id == r.BookingId && b.Pnr == pnr)));
        refund.Status.Should().Be(RefundStatus.Pending);
        refund.RefundAmount.Should().BeGreaterThan(0).And.BeLessThanOrEqualTo(FakeSupplier.Fare);
        (await WalletAsync(pnr)).Should().Be(10_000 - FakeSupplier.Fare, "money only comes back when an admin approves");

        var admin = app.NewBrowser();
        await admin.LoginAsync("admin@skybook.test", "Admin@123");
        (await admin.PostFormAsync($"/Admin/Refunds/Approve/{refund.Id}", "/Admin/Refunds")).StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await WalletAsync(pnr)).Should().Be(10_000 - FakeSupplier.Fare + refund.RefundAmount);
        (await app.QueryAsync(db => db.Bookings.Where(b => b.Pnr == pnr).Select(b => b.Status).SingleAsync())).Should().Be(BookingStatus.Refunded);
    }

    [SqlFact]
    public async Task Same_idempotency_key_creates_one_booking()
    {
        var http = await LoggedInAgentAsync(10_000);
        var (token, fields) = await OpenBookingFormAsync(http);

        var first = PnrFrom(await PostAsync(http, "/Bookings/Create", token, fields));
        var second = PnrFrom(await PostAsync(http, "/Bookings/Create", token, fields));

        second.Should().Be(first);
        var key = Guid.Parse(fields["IdempotencyKey"]);
        (await app.QueryAsync(db => db.Bookings.CountAsync(b => b.IdempotencyKey == key))).Should().Be(1);
    }

    [SqlFact]
    public async Task Parallel_double_submit_creates_one_booking()
    {
        var http = await LoggedInAgentAsync(10_000);
        var (token, fields) = await OpenBookingFormAsync(http);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(http, "/Bookings/Create", token, fields)));

        responses.Select(PnrFrom).Distinct().Should().HaveCount(1);
        var key = Guid.Parse(fields["IdempotencyKey"]);
        (await app.QueryAsync(db => db.Bookings.CountAsync(b => b.IdempotencyKey == key))).Should().Be(1);
    }

    [SqlFact]
    public async Task Invalid_passenger_re_renders_form_and_books_nothing()
    {
        var http = await LoggedInAgentAsync(10_000);
        var (token, fields) = await OpenBookingFormAsync(http);
        fields["Passengers[0].FirstName"] = "";

        var r = await PostAsync(http, "/Bookings/Create", token, fields);

        r.StatusCode.Should().Be(HttpStatusCode.OK);
        var key = Guid.Parse(fields["IdempotencyKey"]);
        (await app.QueryAsync(db => db.Bookings.CountAsync(b => b.IdempotencyKey == key))).Should().Be(0);
    }

    [SqlFact]
    public async Task Ticketing_with_insufficient_balance_is_refused_and_booking_stays_held()
    {
        var http = await LoggedInAgentAsync(wallet: 500);
        var pnr = await BookAsync(http);

        var r = await http.PostFormAsync($"/Bookings/Ticket/{pnr}", $"/Bookings/Details/{pnr}");
        r.StatusCode.Should().Be(HttpStatusCode.Redirect);

        (await (await http.GetAsync($"/Bookings/Details/{pnr}")).BodyAsync()).Should().Contain("Insufficient wallet balance");
        (await app.QueryAsync(db => db.Bookings.Where(b => b.Pnr == pnr).Select(b => b.Status).SingleAsync())).Should().Be(BookingStatus.Held);
        (await WalletAsync(pnr)).Should().Be(500);
    }

    [SqlFact]
    public async Task Ticketing_twice_charges_once()
    {
        var http = await LoggedInAgentAsync(10_000);
        var pnr = await BookAsync(http);
        var details = $"/Bookings/Details/{pnr}";

        await http.PostFormAsync($"/Bookings/Ticket/{pnr}", details);
        await http.PostFormAsync($"/Bookings/Ticket/{pnr}", details);

        (await WalletAsync(pnr)).Should().Be(10_000 - FakeSupplier.Fare);
    }

    [SqlFact]
    public async Task Cancelling_a_held_booking_creates_no_refund()
    {
        var http = await LoggedInAgentAsync(10_000);
        var pnr = await BookAsync(http);

        await http.PostFormAsync($"/Bookings/Cancel/{pnr}", $"/Bookings/Details/{pnr}");

        (await app.QueryAsync(db => db.Bookings.Where(b => b.Pnr == pnr).Select(b => b.Status).SingleAsync())).Should().Be(BookingStatus.Cancelled);
        (await app.QueryAsync(db => db.Refunds.CountAsync(r => db.Bookings.Any(b => b.Id == r.BookingId && b.Pnr == pnr)))).Should().Be(0);
    }

    // ---- data isolation -------------------------------------------------------------------

    [SqlFact]
    public async Task Agent_cannot_see_or_change_another_agents_booking()
    {
        var owner = await LoggedInAgentAsync(10_000);
        var pnr = await BookAsync(owner);

        var other = await LoggedInAgentAsync(10_000);
        (await other.GetAsync($"/Bookings/Details/{pnr}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostFormAsync($"/Bookings/Cancel/{pnr}", "/Bookings")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await other.PostFormAsync($"/Bookings/Ticket/{pnr}", "/Bookings")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await app.QueryAsync(db => db.Bookings.Where(b => b.Pnr == pnr).Select(b => b.Status).SingleAsync())).Should().Be(BookingStatus.Held);
    }

    // ---- concurrency ----------------------------------------------------------------------

    [SqlFact]
    public async Task Parallel_ticketing_never_overspends_the_wallet()
    {
        // wallet covers exactly two of the five bookings; UPDLOCK in usp_IssueTicket must serialize the debits
        var http = await LoggedInAgentAsync(wallet: 2 * FakeSupplier.Fare + 500);
        var pnrs = new List<string>();
        for (var i = 0; i < 5; i++) pnrs.Add(await BookAsync(http));

        var page = await (await http.GetAsync($"/Bookings/Details/{pnrs[0]}")).BodyAsync();
        var token = HttpExtensions.Token(page);
        await Task.WhenAll(pnrs.Select(p => PostAsync(http, $"/Bookings/Ticket/{p}", token, [])));

        var statuses = await app.QueryAsync(db => db.Bookings.Where(b => pnrs.Contains(b.Pnr)).Select(b => b.Status).ToListAsync());
        statuses.Count(s => s == BookingStatus.Ticketed).Should().Be(2);
        statuses.Count(s => s == BookingStatus.Held).Should().Be(3);
        (await WalletAsync(pnrs[0])).Should().Be(500).And.BeGreaterThanOrEqualTo(0);
    }
}
