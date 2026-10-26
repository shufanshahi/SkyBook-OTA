using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SkyBook.Domain.Entities;
using SkyBook.Infrastructure.Persistence;

namespace SkyBook.Web.Data;

public static class DbSeeder
{
    /// <summary>Applies migrations (retrying while SQL Server boots) and seeds demo users.</summary>
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SkyBookDbContext>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<SkyBookDbContext>>();

        for (var attempt = 1; ; attempt++)
        {
            try { await db.Database.MigrateAsync(ct); break; }
            catch (Exception ex) when (attempt < 10)
            {
                log.LogWarning("Database not ready (attempt {Attempt}): {Message}", attempt, ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(3), ct);
            }
        }

        if (await db.Users.AnyAsync(ct)) return;

        var hasher = new PasswordHasher<User>();
        var agents = new[]
        {
            new Agent { AgencyName = "Sky Travels",  Code = "AG001", WalletBalance = 50_000, CreditLimit = 10_000, MarkupPercent = 3 },
            new Agent { AgencyName = "Blue Horizon", Code = "AG002", WalletBalance = 5_000,  CreditLimit = 0,      MarkupPercent = 5 },
        };
        db.Agents.AddRange(agents);
        await db.SaveChangesAsync(ct);

        var admin  = new User { Email = "admin@skybook.test",  Role = "Admin" };
        var agent1 = new User { Email = "agent1@skybook.test", Role = "Agent", AgentId = agents[0].Id };
        var agent2 = new User { Email = "agent2@skybook.test", Role = "Agent", AgentId = agents[1].Id };
        admin.PasswordHash  = hasher.HashPassword(admin,  "Admin@123");
        agent1.PasswordHash = hasher.HashPassword(agent1, "Agent@123");
        agent2.PasswordHash = hasher.HashPassword(agent2, "Agent@123");
        db.Users.AddRange(admin, agent1, agent2);
        await db.SaveChangesAsync(ct);
    }
}
