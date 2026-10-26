using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyBook.Infrastructure.Bookings;
using SkyBook.Web.Areas.Admin.Models;

namespace SkyBook.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin")]
public class SupplierLogsController(AdminService admin) : Controller
{
    // the troubleshooting screen: paste a correlation id (shown on error pages) to see every supplier call of that request
    public async Task<IActionResult> Index(SupplierLogViewModel vm, CancellationToken ct)
    {
        vm.Logs = await admin.SupplierLogsAsync(vm.CorrelationId, vm.Supplier, ct);
        return View(vm);
    }
}
