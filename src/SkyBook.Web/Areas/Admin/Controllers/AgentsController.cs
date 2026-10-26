using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SkyBook.Domain;
using SkyBook.Infrastructure.Bookings;
using SkyBook.Web.Areas.Admin.Models;

namespace SkyBook.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin")]
public class AgentsController(AdminService admin) : Controller
{
    public async Task<IActionResult> Index(string? search, string? status, CancellationToken ct) =>
        View(new AgentListViewModel { Search = search, Status = status, Agents = await admin.ListAgentsAsync(search, status, ct) });

    [HttpGet]
    public IActionResult Create() => View(new AgentCreateViewModel());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(AgentCreateViewModel vm, CancellationToken ct)
    {
        if (!ModelState.IsValid) return View(vm);
        try
        {
            var agent = await admin.CreateAgentAsync(vm.AgencyName, vm.Code, vm.Email, vm.Password, vm.MarkupPercent, vm.CreditLimit, ct);
            TempData["Success"] = $"Agent {agent.Code} created. They can sign in as {vm.Email}.";
            return RedirectToAction(nameof(Index));
        }
        catch (DomainException ex)
        {
            ModelState.AddModelError("", ex.Message);
            return View(vm);
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> TopUp(int id, decimal amount, string? reference, CancellationToken ct) =>
        Act(() => admin.TopUpAsync(id, amount, reference ?? "", ct), $"Wallet topped up by BDT {amount:N0}.");

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Markup(int id, decimal markupPercent, CancellationToken ct) =>
        Act(() => admin.SetMarkupAsync(id, markupPercent, ct), $"Markup set to {markupPercent}%.");

    [HttpPost, ValidateAntiForgeryToken]
    public Task<IActionResult> Status(int id, bool active, CancellationToken ct) =>
        Act(() => admin.SetStatusAsync(id, active, ct), active ? "Agent activated." : "Agent suspended.");

    private async Task<IActionResult> Act(Func<Task> action, string success)
    {
        try { await action(); TempData["Success"] = success; }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (DomainException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
