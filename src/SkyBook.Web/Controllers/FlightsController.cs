using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyBook.Application.Suppliers;
using SkyBook.Infrastructure.Bookings;
using SkyBook.Web.Models;

namespace SkyBook.Web.Controllers;

[Authorize(Roles = "Agent")]
public class FlightsController(FlightSearchService search, AdminService admin) : AgentControllerBase
{
    // GET on purpose: searching has no side effects, so results are bookmarkable and refresh-safe
    [HttpGet]
    public async Task<IActionResult> Search(FlightSearchViewModel vm, CancellationToken ct)
    {
        if (!Request.Query.Any()) return View(new FlightSearchViewModel());   // first visit: empty form
        vm.Searched = true;
        if (!ModelState.IsValid) return View(vm);

        var agent = await admin.GetAgentAsync(AgentId, ct);
        if (agent is null || agent.Status != "Active")
        {
            ModelState.AddModelError("", "Your agency is suspended and cannot search or book.");
            return View(vm);
        }

        vm.Result = await search.SearchAsync(vm.ToRequest(), agent.MarkupPercent, ct);
        return View(vm);
    }
}
