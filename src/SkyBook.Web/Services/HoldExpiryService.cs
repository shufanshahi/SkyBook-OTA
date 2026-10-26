using SkyBook.Infrastructure.Bookings;

namespace SkyBook.Web.Services;

/// <summary>Every minute: cancel held bookings whose hold ran out at the supplier and mark them Expired.</summary>
public class HoldExpiryService(IServiceScopeFactory scopes, ILogger<HoldExpiryService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();     // DbContext is scoped, a hosted service is a singleton
                await scope.ServiceProvider.GetRequiredService<BookingService>().ExpireHeldAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception ex)
            {
                // e.g. database not ready yet: log and try again next minute
                log.LogWarning(ex, "Hold expiry run failed");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
