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
}
