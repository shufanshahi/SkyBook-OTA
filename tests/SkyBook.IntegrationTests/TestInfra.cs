using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SkyBook.Application.Suppliers;
using SkyBook.Domain.Entities;
using SkyBook.Infrastructure.Persistence;

namespace SkyBook.IntegrationTests;

public static class TestDb
{
    public const string EnvVar = "SKYBOOK_TEST_DB";
    private const string Default =
        "Server=localhost,1433;Database=SkyBookTests;User Id=sa;Password=Your_strong_Pass123;TrustServerCertificate=True;Connect Timeout=3";

    public static readonly string ConnectionString = Environment.GetEnvironmentVariable(EnvVar) ?? Default;

    /// <summary>Null when SQL Server answers; otherwise the reason the tests are skipped.</summary>
    public static readonly string? SkipReason = Probe();

    private static string? Probe()
    {
        // CI sets the variable and the SQL Server container may still be booting, so wait for it; locally try once
        var attempts = Environment.GetEnvironmentVariable(EnvVar) is null ? 1 : 30;
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master", ConnectTimeout = 3 }.ConnectionString;
        for (var i = 1; ; i++)
        {
            try { using var c = new SqlConnection(master); c.Open(); return null; }
            catch (Exception ex) when (i < attempts) { _ = ex; Thread.Sleep(2000); }
            catch (Exception ex) { return $"SQL Server is not reachable ({ex.Message.Split('\n')[0]}). Start it with docker compose up -d sqlserver or set {EnvVar}."; }
        }
    }
}

/// <summary>[Fact] that is skipped (not failed) when no SQL Server is reachable.</summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute() { Skip = TestDb.SkipReason; }
}

/// <summary>A deterministic airline so tests never need the Go/SOAP mocks. Every call is counted.</summary>
public sealed class FakeSupplier : IFlightSupplier
{
    public const decimal Fare = 2000m;
    public string Name => "FakeAir";
    public int Booked, Ticketed, Cancelled;

    public Task<IReadOnlyList<FlightOffer>> SearchAsync(FlightSearchRequest req, CancellationToken ct)
    {
        var dep = new DateTimeOffset(req.DepartureDate.ToDateTime(new TimeOnly(9, 0)), TimeSpan.Zero);
        IReadOnlyList<FlightOffer> offers =
        [
            new($"FakeAir:{req.Origin}-{req.Destination}", Name,
                [new Segment("FA", "FA100", req.Origin, req.Destination, dep, dep.AddHours(1))],
                Fare - 300, 300, Fare, "BDT", "Economy", 9, "20kg", true, DateTimeOffset.UtcNow.AddMinutes(15))
        ];
        return Task.FromResult(offers);
    }

    public async Task<PricedOffer> PriceAsync(string offerId, CancellationToken ct)
    {
        var parts = offerId[(Name.Length + 1)..].Split('-');
        return new PricedOffer((await SearchAsync(new FlightSearchRequest(parts[0], parts[1], DateOnly.FromDateTime(DateTime.UtcNow.AddDays(14))), ct))[0]);
    }

    public Task<SupplierBookingResult> BookAsync(SupplierBookingRequest req, CancellationToken ct)
    {
        var n = Interlocked.Increment(ref Booked);
        return Task.FromResult(new SupplierBookingResult($"FA-{n:D5}", $"FA{n:D4}", Fare * req.Passengers.Count, "BDT"));
    }

    public Task<SupplierTicketResult> IssueTicketAsync(string supplierRef, CancellationToken ct)
    {
        Interlocked.Increment(ref Ticketed);
        return Task.FromResult(new SupplierTicketResult(supplierRef, [$"999-{supplierRef}"]));
    }

    public Task<SupplierCancelResult> CancelAsync(string supplierRef, CancellationToken ct)
    {
        Interlocked.Increment(ref Cancelled);
        return Task.FromResult(new SupplierCancelResult(supplierRef, "Cancelled"));
    }
}

/// <summary>Boots the real MVC app in memory against a throwaway SQL Server database with the fake airline.</summary>
public sealed class SkyBookFactory : WebApplicationFactory<Program>
{
    public FakeSupplier Supplier { get; } = new();

    public SkyBookFactory()
    {
        // this database is dropped on every run: refuse anything that does not look like a test database
        var name = new SqlConnectionStringBuilder(TestDb.ConnectionString).InitialCatalog;
        if (!name.Contains("Test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{TestDb.EnvVar} must point at a database whose name contains 'Test' (got '{name}').");
        if (TestDb.SkipReason is not null) return;   // tests are skipped; do not touch the database

        using var db = new SkyBookDbContext(new DbContextOptionsBuilder<SkyBookDbContext>().UseSqlServer(TestDb.ConnectionString).Options);
        db.Database.EnsureDeleted();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");   // Development applies migrations and seeds the demo users on startup
        builder.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = TestDb.ConnectionString }));
        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(IFlightSupplier)).ToList()) services.Remove(d);
            services.AddSingleton<IFlightSupplier>(Supplier);
        });
    }

    protected override void ConfigureClient(HttpClient client) { }

    public HttpClient NewBrowser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    /// <summary>A brand-new agency + login so each test owns its wallet and bookings.</summary>
    public async Task<string> CreateAgentAsync(decimal wallet, decimal credit = 0, decimal markup = 0)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SkyBookDbContext>();
        var id = Guid.NewGuid().ToString("N")[..8];
        var agent = new Agent { AgencyName = $"Test {id}", Code = $"T{id}".ToUpperInvariant(), WalletBalance = wallet, CreditLimit = credit, MarkupPercent = markup };
        var user = new User { Email = $"{id}@test.skybook", Role = "Agent", Agent = agent };
        user.PasswordHash = new PasswordHasher<User>().HashPassword(user, "Agent@123");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Email;
    }

    public async Task<T> QueryAsync<T>(Func<SkyBookDbContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<SkyBookDbContext>());
    }
}

public static class HttpExtensions
{
    private static readonly Regex TokenRx = new("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"", RegexOptions.Compiled);

    public static string Token(string html) =>
        TokenRx.Match(html).Groups[1].Value is { Length: > 0 } t ? t : throw new InvalidOperationException("No anti-forgery token on the page.");

    public static async Task<string> BodyAsync(this HttpResponseMessage r) => await r.Content.ReadAsStringAsync();

    public static async Task LoginAsync(this HttpClient http, string email, string password)
    {
        var page = await (await http.GetAsync("/Account/Login")).BodyAsync();
        var r = await http.PostAsync("/Account/Login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = email, ["Password"] = password, ["__RequestVerificationToken"] = Token(page)
        }));
        if (r.StatusCode != HttpStatusCode.Redirect) throw new InvalidOperationException($"Login failed ({(int)r.StatusCode}).");
    }

    /// <summary>POSTs a form after fetching the anti-forgery token from <paramref name="tokenPage"/>.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(this HttpClient http, string url, string tokenPage,
        Dictionary<string, string>? fields = null)
    {
        var page = await (await http.GetAsync(tokenPage)).BodyAsync();
        var form = new Dictionary<string, string>(fields ?? []) { ["__RequestVerificationToken"] = Token(page) };
        return await http.PostAsync(url, new FormUrlEncodedContent(form));
    }

    /// <summary>Reads every hidden input of the page (OfferId, ExpectedTotal, IdempotencyKey...).</summary>
    public static Dictionary<string, string> HiddenFields(string html) =>
        Regex.Matches(html, "<input[^>]*type=\"hidden\"[^>]*>")
            .Select(m => m.Value)
            .Select(tag => (Name: Regex.Match(tag, "name=\"([^\"]+)\"").Groups[1].Value, Value: System.Net.WebUtility.HtmlDecode(Regex.Match(tag, "value=\"([^\"]*)\"").Groups[1].Value)))
            .Where(f => f.Name != "__RequestVerificationToken")
            .GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First().Value);
}
