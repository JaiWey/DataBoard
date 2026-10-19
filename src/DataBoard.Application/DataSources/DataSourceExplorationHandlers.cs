using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Organizations;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.DataSources;

public sealed record TestConnectionRequest(DataSourceType Type, string ConnectionString);
/// <param name="Parameters">The parameters the SQL takes ({{name}} placeholders); the preview runs them with their defaults.</param>
public sealed record PreviewQueryRequest(string Sql, int? MaxRows, IReadOnlyList<Datasets.DatasetParameter>? Parameters = null);

/// <summary>Shared lookup: the data source must belong to the organization in the URL, and the secret is decrypted only here.</summary>
internal static class DataSourceLookup
{
    public static async Task<(DataSource Source, string ConnectionString)> LoadAsync(
        IDataSourceRepository repository, ISecretProtector secrets, Guid organizationId, Guid dataSourceId, CancellationToken ct)
    {
        var source = await repository.GetAsync(organizationId, dataSourceId, ct)
            ?? throw new NotFoundException("Data source not found.");
        return (source, secrets.Unprotect(source.EncryptedConnectionString));
    }
}

/// <summary>Tests a connection that has not been saved yet. Editors and owners only.</summary>
public sealed class TestNewConnectionHandler(DataSourceConnectors connectors, OrganizationAccess access)
{
    public async Task<ConnectionTestResult> HandleAsync(Guid organizationId, TestConnectionRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        access.RequireNotGuest(); // it would connect to a host the guest picks

        if (string.IsNullOrWhiteSpace(request.ConnectionString))
            throw new ValidationException(nameof(request.ConnectionString), "Connection string is required.");

        var connector = connectors.Get(request.Type);
        connector.ValidateConnectionString(request.ConnectionString);
        return await connector.TestAsync(request.ConnectionString, ct);
    }
}

public sealed class TestSavedConnectionHandler(
    IDataSourceRepository repository, ISecretProtector secrets, DataSourceConnectors connectors, OrganizationAccess access)
{
    public async Task<ConnectionTestResult> HandleAsync(Guid organizationId, Guid dataSourceId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var (source, connectionString) = await DataSourceLookup.LoadAsync(repository, secrets, organizationId, dataSourceId, ct);
        return await connectors.Get(source.Type).TestAsync(connectionString, ct);
    }
}

/// <summary>Browsing the schema is allowed for any member, since viewers need it to understand the data.</summary>
public sealed class GetSchemaHandler(
    IDataSourceRepository repository, ISecretProtector secrets, DataSourceConnectors connectors, OrganizationAccess access)
{
    public async Task<DatabaseSchema> HandleAsync(Guid organizationId, Guid dataSourceId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var (source, connectionString) = await DataSourceLookup.LoadAsync(repository, secrets, organizationId, dataSourceId, ct);
        return await connectors.Get(source.Type).GetSchemaAsync(connectionString, ct);
    }
}

/// <summary>Runs arbitrary SQL, so it needs Editor and always goes through the validator first.</summary>
public sealed class PreviewQueryHandler(
    IDataSourceRepository repository,
    ISecretProtector secrets,
    DataSourceConnectors connectors,
    ISqlQueryValidator validator,
    OrganizationAccess access)
{
    public const int DefaultRows = 50;
    public const int MaxRows = 100;

    public async Task<QueryPreview> HandleAsync(
        Guid organizationId, Guid dataSourceId, PreviewQueryRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var (source, connectionString) = await DataSourceLookup.LoadAsync(repository, secrets, organizationId, dataSourceId, ct);

        var connector = connectors.Get(source.Type);
        Datasets.SqlParameters.CheckDefinitions(request.Sql ?? string.Empty, request.Parameters);
        var bound = Datasets.SqlParameters.Bind(request.Sql ?? string.Empty, request.Parameters ?? [], null);
        var sql = validator.ValidateReadOnly(source.Type, bound.Sql);
        var maxRows = Math.Clamp(request.MaxRows ?? DefaultRows, 1, MaxRows);

        return await connector.PreviewAsync(connectionString, sql, maxRows, bound.Parameters, ct);
    }
}
