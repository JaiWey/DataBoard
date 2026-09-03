using DataBoard.Domain.DataSources;

namespace DataBoard.Application.DataSources;

internal static class DataSourceMapping
{
    public static DataSourceDto ToDto(this DataSource d) =>
        new(d.Id, d.OrganizationId, d.Name, d.Type, d.CreatedUtc);
}
