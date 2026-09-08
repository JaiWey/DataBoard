using DataBoard.Domain.Charts;

namespace DataBoard.Application.Charts;

public interface IChartRepository
{
    Task AddAsync(Chart chart, CancellationToken ct = default);
    void Remove(Chart chart);

    /// <summary>Returns a tracked entity, or null if it is not in that organization.</summary>
    Task<Chart?> GetAsync(Guid organizationId, Guid chartId, CancellationToken ct = default);

    Task<IReadOnlyList<Chart>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludeChartId, CancellationToken ct = default);
}
