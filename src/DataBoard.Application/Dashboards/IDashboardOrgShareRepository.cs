using DataBoard.Domain.Dashboards;

namespace DataBoard.Application.Dashboards;

public interface IDashboardOrgShareRepository
{
    Task AddAsync(DashboardOrgShare share, CancellationToken ct = default);
    void Remove(DashboardOrgShare share);

    /// <summary>The share of this dashboard with this organization, if there is one.</summary>
    Task<DashboardOrgShare?> GetAsync(Guid dashboardId, Guid targetOrganizationId, CancellationToken ct = default);

    /// <summary>Every organization this dashboard is shared with.</summary>
    Task<IReadOnlyList<DashboardOrgShare>> ListForDashboardAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default);

    /// <summary>Everything shared with this organization, from any other.</summary>
    Task<IReadOnlyList<DashboardOrgShare>> ListForTargetAsync(Guid targetOrganizationId, CancellationToken ct = default);

    Task<int> CountForDashboardAsync(Guid dashboardId, CancellationToken ct = default);
}
