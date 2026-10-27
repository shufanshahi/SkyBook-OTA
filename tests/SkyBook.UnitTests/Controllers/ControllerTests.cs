using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SkyBook.Application.Suppliers;
using SkyBook.Domain.Entities;
using SkyBook.Infrastructure.Bookings;
using SkyBook.Infrastructure.Persistence;
using SkyBook.UnitTests.Suppliers;
using SkyBook.Web.Controllers;
using SkyBook.Web.Models;

namespace SkyBook.UnitTests.Controllers;

public class ControllerTests
{
    private static SkyBookDbContext NewDb() =>
        new(new DbContextOptionsBuilder<SkyBookDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static T WithAgent<T>(T controller, int agentId) where T : Controller
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("AgentId", agentId.ToString())], "test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        controller.TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>());
        return controller;
    }

    private static async Task<(FlightsController Controller, FakeOfferCache Cache)> FlightsAsync(string agentStatus = "Active", params FlightOffer[] offers)
    {
        var db = NewDb();
        db.Agents.Add(new Agent { Id = 1, AgencyName = "Test", Code = "T1", Status = agentStatus, MarkupPercent = 10 });
        await db.SaveChangesAsync();
        var cache = new FakeOfferCache();
        var search = new FlightSearchService([TestData.Supplier("AeroNDC", offers)], cache, NullLogger<FlightSearchService>.Instance);
        return (WithAgent(new FlightsController(search, new AdminService(db, new PasswordHasher<User>())), 1), cache);
    }

    private static FlightSearchViewModel ValidSearch() => new() { Origin = "DAC", Destination = "CGP", DepartureDate = DateTime.Today.AddDays(7) };

    // ---- FlightsController ----------------------------------------------------------------

    [Fact]
    public async Task Search_without_query_shows_empty_form()
    {
        var (c, _) = await FlightsAsync();

        var result = await c.Search(new FlightSearchViewModel(), default);

        var view = result.Should().BeOfType<ViewResult>().Subject;
        view.Model.Should().BeOfType<FlightSearchViewModel>().Which.Searched.Should().BeFalse();
    }

    [Fact]
    public async Task Search_with_invalid_model_returns_same_view_without_calling_suppliers()
    {
        var (c, cache) = await FlightsAsync(offers: TestData.Offer());
        c.Request.QueryString = new QueryString("?Origin=DAC");
        c.ModelState.AddModelError("Destination", "required");

        var result = await c.Search(new FlightSearchViewModel(), default);

        result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<FlightSearchViewModel>().Which.Result.Should().BeNull();
        c.ModelState.IsValid.Should().BeFalse();
        cache.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_applies_the_agents_markup()
    {
        var (c, _) = await FlightsAsync(offers: TestData.Offer(total: 1000));
        c.Request.QueryString = new QueryString("?Origin=DAC");

        var result = await c.Search(ValidSearch(), default);

        var vm = result.Should().BeOfType<ViewResult>().Which.Model.Should().BeOfType<FlightSearchViewModel>().Subject;
        vm.Result!.Offers.Should().ContainSingle().Which.Total.Should().Be(1100);   // 1000 + 10%
    }

    [Fact]
    public async Task Suspended_agent_cannot_search()
    {
        var (c, cache) = await FlightsAsync("Suspended", TestData.Offer());
        c.Request.QueryString = new QueryString("?Origin=DAC");

        var result = await c.Search(ValidSearch(), default);

        result.Should().BeOfType<ViewResult>();
        c.ModelState.Values.SelectMany(v => v.Errors).Should().Contain(e => e.ErrorMessage.Contains("suspended"));
        cache.Items.Should().BeEmpty();
    }

    // ---- AccountController ----------------------------------------------------------------

    private static (AccountController Controller, SkyBookDbContext Db) Account()
    {
        var db = NewDb();
        var hasher = new PasswordHasher<User>();
        var user = new User { Email = "a@test.com", Role = "Agent", AgentId = 1 };
        user.PasswordHash = hasher.HashPassword(user, "Secret@1");
        db.Users.Add(user);
        db.SaveChanges();

        var c = new AccountController(db, hasher);
        var services = Substitute.For<IServiceProvider>();
        services.GetService(typeof(Microsoft.AspNetCore.Authentication.IAuthenticationService))
            .Returns(Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationService>());
        var http = new DefaultHttpContext { RequestServices = services };
        c.ControllerContext = new ControllerContext { HttpContext = http };
        c.TempData = new TempDataDictionary(http, Substitute.For<ITempDataProvider>());
        c.Url = Substitute.For<IUrlHelper>();
        return (c, db);
    }

    [Fact]
    public async Task Login_with_wrong_password_returns_view_with_generic_error()
    {
        var (c, _) = Account();

        var result = await c.Login(new LoginViewModel { Email = "a@test.com", Password = "wrong" }, default);

        result.Should().BeOfType<ViewResult>();
        c.ModelState[""]!.Errors.Single().ErrorMessage.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Login_with_unknown_email_gives_the_same_error_as_wrong_password()
    {
        var (c, _) = Account();

        await c.Login(new LoginViewModel { Email = "nobody@test.com", Password = "x" }, default);

        c.ModelState[""]!.Errors.Single().ErrorMessage.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Login_success_redirects_home()
    {
        var (c, _) = Account();

        var result = await c.Login(new LoginViewModel { Email = "A@Test.com ", Password = "Secret@1" }, default);

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Index");
    }

    [Fact]
    public async Task Login_does_not_redirect_to_external_return_url()
    {
        var (c, _) = Account();
        c.Url.IsLocalUrl("https://evil.example/").Returns(false);

        var result = await c.Login(new LoginViewModel { Email = "a@test.com", Password = "Secret@1", ReturnUrl = "https://evil.example/" }, default);

        result.Should().BeOfType<RedirectToActionResult>();
    }

    // ---- BookingsController ---------------------------------------------------------------

    private static BookingsController Bookings(SkyBookDbContext db, FakeOfferCache cache)
    {
        var suppliers = new List<IFlightSupplier>();
        var pricing = new FlightPricingService(suppliers, cache);
        var service = new BookingService(db, pricing, suppliers, cache, sql: null!, NullLogger<BookingService>.Instance);
        return WithAgent(new BookingsController(service, pricing, new AdminService(db, new PasswordHasher<User>()), cache), 1);
    }

    [Fact]
    public async Task Details_of_unknown_pnr_is_404()
    {
        var result = await Bookings(NewDb(), new FakeOfferCache()).Details("ZZZZZZ", default);

        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task Create_post_with_expired_offer_redirects_to_search_with_message()
    {
        var c = Bookings(NewDb(), new FakeOfferCache());

        var result = await c.Create(new BookingCreateViewModel { OfferId = "AeroNDC:gone" }, default);

        result.Should().BeOfType<RedirectToActionResult>().Which.ActionName.Should().Be("Search");
        c.TempData["Error"].Should().NotBeNull();
    }
}
