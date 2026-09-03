using DataBoard.Domain.DataSources;

namespace DataBoard.Application.DataSources;

public interface IDataSourceRepository
{
    Task AddAsync(DataSource dataSource, CancellationToken ct = default);
    Task<IReadOnlyList<DataSource>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    /// <summary>Returns null when the id does not exist in that organization.</summary>
    Task<DataSource?> GetAsync(Guid organizationId, Guid dataSourceId, CancellationToken ct = default);
}
