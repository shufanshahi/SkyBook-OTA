using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyBook.Infrastructure.Queries;
using SkyBook.Web.Areas.Admin.Models;

namespace SkyBook.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin")]
public class ReportsController(BookingQueries queries) : Controller
{
    public async Task<IActionResult> Sales(SalesReportViewModel vm, CancellationToken ct)
    {
        vm.Rows = await queries.SalesReportAsync(vm.From, vm.To?.AddDays(1), ct);
        return View(vm);
    }
}
