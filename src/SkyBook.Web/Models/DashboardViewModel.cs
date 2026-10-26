using SkyBook.Domain.Entities;
using SkyBook.Infrastructure.Queries;

namespace SkyBook.Web.Models;

public class DashboardViewModel
{
    public bool IsAdmin { get; set; }
    // agent
    public Agent? Agent { get; set; }
    public IReadOnlyList<BookingRow> Recent { get; set; } = [];
    // admin
    public int AgentCount { get; set; }
    public int BookingsToday { get; set; }
    public int PendingRefunds { get; set; }
}
