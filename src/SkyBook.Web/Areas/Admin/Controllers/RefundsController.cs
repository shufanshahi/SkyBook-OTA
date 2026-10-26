using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyBook.Domain;
using SkyBook.Infrastructure.Bookings;

namespace SkyBook.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin")]
public class RefundsController(AdminService admin) : Controller
{
    public async Task<IActionResult> Index(bool all = false, CancellationToken ct = default)
    {
        ViewBag.All = all;
        return View(await admin.ListRefundsAsync(pendingOnly: !all, ct));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(long id, CancellationToken ct)
    {
        try
        {
            await admin.ApproveRefundAsync(id, ct);
            TempData["Success"] = "Refund approved and credited to the agent's wallet.";
        }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (DomainException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
