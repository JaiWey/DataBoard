using DataBoard.Domain.Dashboards;

namespace DataBoard.Application.Dashboards;

public interface IDashboardShareRepository
{
    Task AddAsync(DashboardShare share, CancellationToken ct = default);
    void Remove(DashboardShare share);

    Task<DashboardShare?> GetAsync(Guid organizationId, Guid dashboardId, Guid shareId, CancellationToken ct = default);

    Task<IReadOnlyList<DashboardShare>> ListAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default);

    /// <summary>The links of a dashboard that still work, to keep their number small.</summary>
    Task<int> CountActiveAsync(Guid dashboardId, DateTime nowUtc, CancellationToken ct = default);

    /// <summary>Finds a link by the hash of its token, whatever organization it belongs to.</summary>
    Task<DashboardShare?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);
}
