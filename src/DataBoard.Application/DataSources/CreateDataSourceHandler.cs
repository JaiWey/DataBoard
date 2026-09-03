using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.DataSources;

public sealed class CreateDataSourceHandler(
    IDataSourceRepository repository,
    IUnitOfWork unitOfWork,
    ISecretProtector secrets,
    IClock clock,
    OrganizationAccess access,
    DataSourceConnectors connectors)
{
    public async Task<DataSourceDto> HandleAsync(CreateDataSourceRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(request.OrganizationId, OrganizationRole.Editor, ct);

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            throw new ValidationException(nameof(request.ConnectionString), "Connection string is required.");

        // Only types we can actually connect to are accepted, and unsafe connection string options are rejected up front.
        connectors.Get(request.Type).ValidateConnectionString(request.ConnectionString);

        DataSource dataSource;
        try
        {
            dataSource = DataSource.Create(
                request.OrganizationId,
                request.Name,
                request.Type,
                secrets.Protect(request.ConnectionString),
                clock.UtcNow);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.ParamName ?? "request", ex.Message);
        }

        await repository.AddAsync(dataSource, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return dataSource.ToDto();
    }
}
