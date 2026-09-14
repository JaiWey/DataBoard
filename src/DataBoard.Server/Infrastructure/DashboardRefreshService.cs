using DataBoard.Application.Abstractions;
using DataBoard.Application.Dashboards;

namespace DataBoard.Server.Infrastructure;

/// <summary>Settings of the scheduled refresh (the "Refresh" section of the configuration).</summary>
public sealed class DashboardRefreshOptions
{
    public const string SectionName = "Refresh";

    /// <summary>Turn the background refresh off, for example on a server that should only answer requests.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often to look for dashboards that are due, in seconds.</summary>
    public int PollSeconds { get; set; } = 30;

    /// <summary>The most dashboards one pass refreshes. The rest wait for the next pass, which keeps the load on the databases even.</summary>
    public int BatchSize { get; set; } = 5;
}

/// <summary>
/// Looks, every few seconds, for dashboards whose refresh is due and refreshes them. It can run on every server at once:
/// a due dashboard is claimed by one server only (see <see cref="IDashboardRepository.ClaimDueAsync"/>). A failed pass is
/// logged and tried again at the next one; it never stops the service.
/// </summary>
public sealed class DashboardRefreshService(
    IServiceScopeFactory scopes, IClock clock, Microsoft.Extensions.Options.IOptions<DashboardRefreshOptions> options, ILogger<DashboardRefreshService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("Scheduled dashboard refresh is turned off.");
            return;
        }

        var wait = TimeSpan.FromSeconds(Math.Max(1, options.Value.PollSeconds));
        using var timer = new PeriodicTimer(wait);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "A pass of the scheduled dashboard refresh failed. It will be tried again.");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    /// <summary>One pass, in its own scope as a web request would have: its own database context and handlers.</summary>
    public async Task<RefreshSummary> RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var summary = await scope.ServiceProvider.GetRequiredService<RefreshDueDashboardsHandler>()
            .HandleAsync(clock.UtcNow, Math.Max(1, options.Value.BatchSize), ct);

        if (summary.Claimed > 0)
            logger.LogInformation("Refreshed dashboards: {Claimed} due, {Refreshed} fully refreshed, {Problems} with problems.",
                summary.Claimed, summary.Refreshed, summary.Problems);
        return summary;
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
