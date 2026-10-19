using DataBoard.Application.Identity;

namespace DataBoard.Server.Infrastructure;

/// <summary>
/// Once an hour, deletes guest accounts whose time is up, together with their sandbox organizations. A failed pass is
/// logged and tried again at the next one; it never stops the service.
/// </summary>
public sealed class GuestCleanupService(IServiceScopeFactory scopes, ILogger<GuestCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                var removed = await RunOnceAsync(stoppingToken);
                if (removed > 0) logger.LogInformation("Deleted {Count} expired guests.", removed);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A pass of the guest cleanup failed. It will be tried again.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IIdentityService>().DeleteExpiredGuestsAsync(ct);
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try { return await timer.WaitForNextTickAsync(ct); }
        catch (OperationCanceledException) { return false; }
    }
}
