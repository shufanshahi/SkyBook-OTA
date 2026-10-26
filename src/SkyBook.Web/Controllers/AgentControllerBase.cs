using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace SkyBook.Web.Controllers;

/// <summary>Agent id always comes from the signed-in user's claims, never from a form or URL.</summary>
public abstract class AgentControllerBase : Controller
{
    protected int AgentId => int.Parse(User.FindFirstValue("AgentId")
        ?? throw new InvalidOperationException("This user is not linked to an agent."));
}
