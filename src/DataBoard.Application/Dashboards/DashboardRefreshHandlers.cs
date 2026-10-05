using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Dashboards;

/// <summary>
/// Puts a dashboard on a refresh schedule, or takes it off. It limits how many dashboards of an organization can be on
/// one, because each refresh asks the customers' databases for every chart on it.
/// </summary>
public sealed class SetDashboardRefreshHandler(IDashboardRepository dashboards, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access, DashboardVisibility visibility)
{
    public const int MaxScheduledPerOrganization = 20;

    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, SetDashboardRefreshRequest request, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);

        if (request.IntervalMinutes is not null && dashboard.RefreshIntervalMinutes is null
            && await dashboards.CountScheduledAsync(organizationId, ct) >= MaxScheduledPerOrganization)
            throw new ValidationException("IntervalMinutes",
                $"At most {MaxScheduledPerOrganization} dashboards of an organization can be refreshed on a schedule. Turn one off first.");

        DatasetQuery.Guard(() =>
        {
            dashboard.SetRefreshInterval(request.IntervalMinutes, clock.UtcNow);
            return true;
        });

        await unitOfWork.SaveChangesAsync(ct);
        return dashboard.ToDto();
    }
}

/// <summary>What one pass of the scheduler did.</summary>
/// <param name="Claimed">Dashboards that were due and taken by this pass.</param>
/// <param name="Refreshed">Those whose charts were all refreshed.</param>
/// <param name="Problems">Those where at least one chart (or the whole refresh) failed; each is recorded on the dashboard.</param>
public sealed record RefreshSummary(int Claimed, int Refreshed, int Problems);

/// <summary>
/// One pass of the scheduled refresh: takes the dashboards that are due (so that no other server takes the same ones),
/// fetches fresh data for each of their charts, stores it for the viewers who follow, and records on the dashboard how it
/// went. A dashboard that fails is simply tried again at its next interval, and never stops the others.
/// </summary>
public sealed class RefreshDueDashboardsHandler(
    IDashboardRepository dashboards, RunDashboardHandler runner, IUnitOfWork unitOfWork)
{
    /// <param name="nowUtc">The time to judge "due" by, and to record.</param>
    /// <param name="max">At most this many dashboards in one pass; the rest wait for the next.</param>
    public async Task<RefreshSummary> HandleAsync(DateTime nowUtc, int max, CancellationToken ct = default)
    {
        var due = await dashboards.ClaimDueAsync(nowUtc, Math.Max(1, max), ct);
        int refreshed = 0, problems = 0;

        foreach (var item in due)
        {
            ct.ThrowIfCancellationRequested();
            var dashboard = await dashboards.GetAsync(item.OrganizationId, item.Id, ct);
            if (dashboard?.RefreshIntervalMinutes is not { } interval) continue; // deleted, or taken off the schedule, since it was claimed

            int failed;
            string? message;
            try
            {
                var run = await runner.HandleScheduledAsync(
                    item.OrganizationId, DashboardJson.Deserialize(dashboard.DefinitionJson), RunDashboardHandler.ScheduledKeepTime(interval), ct);
                var errors = run.Widgets.Where(w => w.Error is not null).ToList();
                failed = errors.Count;
                message = errors.FirstOrDefault()?.Error;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Something the per-chart handling does not cover: say that the refresh did not finish, and go on to the next.
                failed = Math.Max(1, DashboardJson.Deserialize(dashboard.DefinitionJson).Widgets.Count); // none of its charts were refreshed
                message = "The refresh did not finish.";
            }

            dashboard.RecordRefresh(nowUtc, failed, message);
            await unitOfWork.SaveChangesAsync(ct);
            if (failed == 0) refreshed++;
            else problems++;
        }

        return new RefreshSummary(due.Count, refreshed, problems);
    }
}
