using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyBook.Infrastructure.Bookings;
using SkyBook.Infrastructure.Queries;
using SkyBook.Web.Areas.Admin.Models;

namespace SkyBook.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin")]
public class BookingsController(BookingQueries queries, AdminService admin) : Controller
{
    // all-agents search through usp_SearchBookings (Dapper)
    public async Task<IActionResult> Index(AdminBookingSearchViewModel vm, CancellationToken ct)
    {
        vm.Agents = await admin.ListAgentsAsync(null, null, ct);
        vm.Page = Math.Max(1, vm.Page);
        vm.Searched = Request.Query.Any();

        var pnr = string.IsNullOrWhiteSpace(vm.Pnr) ? null : vm.Pnr.Trim().ToUpperInvariant();
        vm.Rows = await queries.SearchAsync(new BookingSearchFilter(
            vm.AgentId, string.IsNullOrWhiteSpace(vm.Status) ? null : vm.Status, pnr,
            vm.From, vm.To?.AddDays(1),               // "to" is a date, include that whole day
            vm.Page, vm.PageSize), ct);
        return View(vm);
    }
}
