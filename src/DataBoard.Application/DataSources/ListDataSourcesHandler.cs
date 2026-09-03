using DataBoard.Application.Organizations;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.DataSources;

public sealed class ListDataSourcesHandler(IDataSourceRepository repository, OrganizationAccess access)
{
    public async Task<IReadOnlyList<DataSourceDto>> HandleAsync(Guid organizationId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);

        var items = await repository.ListByOrganizationAsync(organizationId, ct);
        return items.Select(d => d.ToDto()).ToList();
    }
}
