using DataBoard.Domain.Datasets;

namespace DataBoard.Application.Datasets;

public interface IDatasetRepository
{
    Task AddAsync(Dataset dataset, CancellationToken ct = default);
    void Remove(Dataset dataset);

    /// <summary>Returns a tracked entity (so it can be updated or removed), or null if it is not in that organization.</summary>
    Task<Dataset?> GetAsync(Guid organizationId, Guid datasetId, CancellationToken ct = default);

    Task<IReadOnlyList<Dataset>> ListByOrganizationAsync(Guid organizationId, CancellationToken ct = default);

    Task<bool> NameExistsAsync(Guid organizationId, string name, Guid? excludeDatasetId, CancellationToken ct = default);
}
