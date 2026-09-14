using DataBoard.Domain.Dashboards;

namespace DataBoard.Application.Dashboards;

public interface IDashboardRepository
{
    Task AddAsync(Dashboard dashboard, CancellationToken ct = default);
    void Remove(Dashboard dashboard);

    /// <summary>Returns a tracked entity, or null if it is not in that organization.</summary>
    Task<Dashboard?> GetAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default);

    Task<IReadOnlyList<Dashboard>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludeDashboardId, CancellationToken ct = default);

    /// <summary>How many dashboards of the organization are refreshed on a schedule.</summary>
    Task<int> CountScheduledAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>
    /// Takes up to <paramref name="max"/> dashboards whose refresh is due and pushes each one's next due time an interval
    /// ahead, in one atomic step. Several servers may call this at once: a dashboard is handed to only one of them, so
    /// it is never refreshed twice for one due time. If the server that took it dies, the next try is an interval later.
    /// </summary>
    Task<IReadOnlyList<DueDashboard>> ClaimDueAsync(DateTime nowUtc, int max, CancellationToken ct = default);
}

public sealed record DueDashboard(Guid Id, Guid OrganizationId);
