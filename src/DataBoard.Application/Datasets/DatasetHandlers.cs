using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Datasets;

/// <param name="Sql">Required for SQL datasets. For a table view it is ignored: the SQL is generated from <paramref name="Definition"/>.</param>
public sealed record CreateDatasetRequest(
    Guid DataSourceId, string Name, string? Description, string? Sql,
    DatasetKind Kind = DatasetKind.Sql, TableViewDefinition? Definition = null);

/// <summary>The kind of a dataset cannot change, so an update carries the SQL or the definition, whichever the dataset uses.</summary>
public sealed record UpdateDatasetRequest(string Name, string? Description, string? Sql, TableViewDefinition? Definition = null);

public sealed record DatasetColumnDto(string Name, string DataType, ColumnRole Role);

/// <param name="Sql">Only Editors and Owners get the SQL and the table view definition; Viewers see the dataset's shape and data, not how it is built.</param>
public sealed record DatasetDto(
    Guid Id,
    Guid DataSourceId,
    string Name,
    string? Description,
    string? Sql,
    IReadOnlyList<DatasetColumnDto> Columns,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DatasetKind Kind = DatasetKind.Sql,
    TableViewDefinition? Definition = null);

internal static class DatasetMapping
{
    public static DatasetDto ToDto(this Dataset d, bool includeSql) => new(
        d.Id,
        d.DataSourceId,
        d.Name,
        d.Description,
        includeSql ? d.Sql : null,
        d.Columns.Select(c => new DatasetColumnDto(c.Name, c.DataType, c.Role)).ToList(),
        d.CreatedUtc,
        d.UpdatedUtc,
        d.Kind,
        includeSql ? TableViewDefinition.FromJson(d.DefinitionJson) : null);
}

/// <summary>Validates SQL and records what it returns. Shared by create and update.</summary>
internal static class DatasetQuery
{
    public static async Task<(string Sql, List<DatasetColumn> Columns)> PrepareAsync(
        IDataSourceConnector connector,
        ISqlQueryValidator validator,
        Domain.DataSources.DataSource source,
        string connectionString,
        string sql,
        CancellationToken ct)
    {
        var validated = validator.ValidateReadOnly(source.Type, sql);
        var described = await connector.DescribeAsync(connectionString, validated, ct);
        var columns = described
            .Select(c => new DatasetColumn(c.Name, c.DataType, ColumnRoleInference.Infer(c.DataType)))
            .ToList();
        return (validated, columns);
    }

    /// <summary>
    /// Works out the SQL and columns for a create or update. A SQL dataset uses what was typed; a table view is checked
    /// against the data source's real schema and its SQL is generated, then goes through the same validation as typed SQL.
    /// </summary>
    public static async Task<(string Sql, List<DatasetColumn> Columns, string? DefinitionJson)> ResolveAsync(
        DatasetKind kind,
        string? sql,
        TableViewDefinition? definition,
        IDataSourceConnector connector,
        TableViewSqlRenderers renderers,
        ISqlQueryValidator validator,
        Domain.DataSources.DataSource source,
        string connectionString,
        CancellationToken ct)
    {
        switch (kind)
        {
            case DatasetKind.Sql:
                if (definition is not null)
                    throw new ValidationException("Definition", "A SQL dataset has no table view definition.");
                var (typed, typedColumns) = await PrepareAsync(connector, validator, source, connectionString, sql ?? string.Empty, ct);
                return (typed, typedColumns, null);

            case DatasetKind.TableView:
                var generated = await GenerateAsync(definition, connector, renderers, source, connectionString, ct);
                var (checkedSql, columns) = await PrepareAsync(connector, validator, source, connectionString, generated, ct);
                return (checkedSql, columns, definition!.ToJson());

            default:
                throw new ValidationException("Kind", "The dataset kind is not valid.");
        }
    }

    /// <summary>The SQL a table view definition stands for. Checks the definition against the live schema first.</summary>
    public static async Task<string> GenerateAsync(
        TableViewDefinition? definition,
        IDataSourceConnector connector,
        TableViewSqlRenderers renderers,
        Domain.DataSources.DataSource source,
        string connectionString,
        CancellationToken ct)
    {
        var schema = await connector.GetSchemaAsync(connectionString, ct);
        var plan = TableViewPlanner.Plan(schema, definition);
        return renderers.Get(source.Type).Render(plan);
    }

    /// <summary>Domain rules report problems as ArgumentException; callers see validation errors.</summary>
    public static T Guard<T>(Func<T> build)
    {
        try
        {
            return build();
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(ex.ParamName ?? "request", ex.Message);
        }
    }
}

public sealed class CreateDatasetHandler(
    IDatasetRepository datasets,
    IDataSourceRepository dataSources,
    ISecretProtector secrets,
    DataSourceConnectors connectors,
    TableViewSqlRenderers renderers,
    ISqlQueryValidator validator,
    IUnitOfWork unitOfWork,
    IClock clock,
    OrganizationAccess access)
{
    public async Task<DatasetDto> HandleAsync(Guid organizationId, CreateDatasetRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();

        var (source, connectionString) = await DataSourceLookup.LoadAsync(dataSources, secrets, organizationId, request.DataSourceId, ct);
        var (sql, columns, definitionJson) = await DatasetQuery.ResolveAsync(
            request.Kind, request.Sql, request.Definition, connectors.Get(source.Type), renderers, validator, source, connectionString, ct);

        if (await datasets.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, null, ct))
            throw new ConflictException("A dataset with that name already exists.");

        var dataset = DatasetQuery.Guard(() => Dataset.Create(
            organizationId, source.Id, request.Name!, request.Description, sql, columns, userId, clock.UtcNow,
            request.Kind, definitionJson));

        await datasets.AddAsync(dataset, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return dataset.ToDto(includeSql: true);
    }
}

public sealed class ListDatasetsHandler(IDatasetRepository datasets, OrganizationAccess access)
{
    public async Task<IReadOnlyList<DatasetDto>> HandleAsync(Guid organizationId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var canSeeSql = member.HasAtLeast(OrganizationRole.Editor);

        var items = await datasets.ListByOrganizationAsync(organizationId, ct);
        return items.Select(d => d.ToDto(canSeeSql)).ToList();
    }
}

public sealed class GetDatasetHandler(IDatasetRepository datasets, OrganizationAccess access)
{
    public async Task<DatasetDto> HandleAsync(Guid organizationId, Guid datasetId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var dataset = await datasets.GetAsync(organizationId, datasetId, ct)
            ?? throw new NotFoundException("Dataset not found.");
        return dataset.ToDto(member.HasAtLeast(OrganizationRole.Editor));
    }
}

public sealed class UpdateDatasetHandler(
    IDatasetRepository datasets,
    IDataSourceRepository dataSources,
    ISecretProtector secrets,
    DataSourceConnectors connectors,
    TableViewSqlRenderers renderers,
    ISqlQueryValidator validator,
    IUnitOfWork unitOfWork,
    IClock clock,
    OrganizationAccess access)
{
    public async Task<DatasetDto> HandleAsync(Guid organizationId, Guid datasetId, UpdateDatasetRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dataset = await datasets.GetAsync(organizationId, datasetId, ct)
            ?? throw new NotFoundException("Dataset not found.");
        var (source, connectionString) = await DataSourceLookup.LoadAsync(dataSources, secrets, organizationId, dataset.DataSourceId, ct);
        var (sql, columns, definitionJson) = await DatasetQuery.ResolveAsync(
            dataset.Kind, request.Sql, request.Definition, connectors.Get(source.Type), renderers, validator, source, connectionString, ct);

        if (await datasets.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, dataset.Id, ct))
            throw new ConflictException("A dataset with that name already exists.");

        DatasetQuery.Guard(() =>
        {
            dataset.Update(request.Name!, request.Description, sql, columns, clock.UtcNow, definitionJson);
            return true;
        });

        await unitOfWork.SaveChangesAsync(ct);
        return dataset.ToDto(includeSql: true);
    }
}

public sealed class DeleteDatasetHandler(IDatasetRepository datasets, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task HandleAsync(Guid organizationId, Guid datasetId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var dataset = await datasets.GetAsync(organizationId, datasetId, ct)
            ?? throw new NotFoundException("Dataset not found.");

        datasets.Remove(dataset);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Any member can see a dataset's data (that is what dashboards are for), but never its SQL, and the stored SQL
/// is validated again before every run in case what is stored was ever changed outside the application.
/// </summary>
public sealed class PreviewDatasetHandler(
    IDatasetRepository datasets,
    IDataSourceRepository dataSources,
    ISecretProtector secrets,
    DataSourceConnectors connectors,
    ISqlQueryValidator validator,
    OrganizationAccess access)
{
    public async Task<QueryPreview> HandleAsync(Guid organizationId, Guid datasetId, int? maxRows, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);

        var dataset = await datasets.GetAsync(organizationId, datasetId, ct)
            ?? throw new NotFoundException("Dataset not found.");
        var (source, connectionString) = await DataSourceLookup.LoadAsync(dataSources, secrets, organizationId, dataset.DataSourceId, ct);

        var sql = validator.ValidateReadOnly(source.Type, dataset.Sql);
        var rows = Math.Clamp(maxRows ?? PreviewQueryHandler.DefaultRows, 1, PreviewQueryHandler.MaxRows);
        return await connectors.Get(source.Type).PreviewAsync(connectionString, sql, rows, ct);
    }
}

/// <summary>
/// Runs a table view definition without saving it, for the builder's "run this step" and "run" buttons. Editors and
/// Owners only, because it reads the data source's tables directly. The definition is checked against the real schema
/// and the generated SQL passes the same read-only validation as any other query.
/// </summary>
public sealed class PreviewTableViewHandler(
    IDataSourceRepository dataSources,
    ISecretProtector secrets,
    DataSourceConnectors connectors,
    TableViewSqlRenderers renderers,
    ISqlQueryValidator validator,
    OrganizationAccess access)
{
    public async Task<TableViewPreviewResult> HandleAsync(
        Guid organizationId, Guid dataSourceId, PreviewTableViewRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var (source, connectionString) = await DataSourceLookup.LoadAsync(dataSources, secrets, organizationId, dataSourceId, ct);

        var connector = connectors.Get(source.Type);
        var generated = await DatasetQuery.GenerateAsync(request.Definition, connector, renderers, source, connectionString, ct);
        var sql = validator.ValidateReadOnly(source.Type, generated);
        var maxRows = Math.Clamp(request.MaxRows ?? PreviewQueryHandler.DefaultRows, 1, PreviewQueryHandler.MaxRows);

        var result = await connector.PreviewAsync(connectionString, sql, maxRows, ct);
        return new TableViewPreviewResult(result.Columns, result.Rows, result.Truncated, sql);
    }
}

public sealed record PreviewTableViewRequest(TableViewDefinition? Definition, int? MaxRows);
