using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyBook.Application.Suppliers;
using SkyBook.Infrastructure.Bookings;
using SkyBook.Web.Infrastructure;
using SkyBook.Web.Models;

namespace SkyBook.Web.Controllers;

public class HomeController(AdminService admin, BookingService bookings) : Controller
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        if (User.IsInRole("Admin"))
        {
            var (agents, today, pending) = await admin.DashboardAsync(ct);
            return View(new DashboardViewModel { IsAdmin = true, AgentCount = agents, BookingsToday = today, PendingRefunds = pending });
        }

        var agentId = int.Parse(User.FindFirstValue("AgentId")!);
        return View(new DashboardViewModel
        {
            Agent = await admin.GetAgentAsync(agentId, ct),
            Recent = await bookings.ListAsync(agentId, null, 1, 5, ct)
        });
    }

    [AllowAnonymous]
    public IActionResult Error() =>
        View(new ErrorViewModel { RequestId = HttpContext.Items[CorrelationIdMiddleware.Header] as string ?? CorrelationContext.Id });
}
