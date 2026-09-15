using DataBoard.Domain.DataSources;

namespace DataBoard.Application.DataSources;

public interface IDataSourceRepository
{
    Task AddAsync(DataSource dataSource, CancellationToken ct = default);
    Task<IReadOnlyList<DataSource>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>Returns null when the id does not exist in that organization.</summary>
    Task<DataSource?> GetAsync(Guid organizationId, Guid dataSourceId, CancellationToken ct = default);

    /// <summary>Like <see cref="GetAsync"/>, but the entity is tracked, so a change to it can be saved.</summary>
    Task<DataSource?> GetForUpdateAsync(Guid organizationId, Guid dataSourceId, CancellationToken ct = default);

    /// <summary>Removes the data source. The datasets built on it, and the charts built on those, go with it.</summary>
    void Remove(DataSource dataSource);
}
