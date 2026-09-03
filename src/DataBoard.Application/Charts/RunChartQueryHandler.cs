using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Charts;

/// <summary>
/// Runs a chart query over a dataset. Any member may run it (that is what dashboards do), but only Editors and
/// Owners get the generated SQL back, because it contains the dataset's SQL.
/// </summary>
public sealed class RunChartQueryHandler(
    IDatasetRepository datasets,
    IDataSourceRepository dataSources,
    ISecretProtector secrets,
    DataSourceConnectors connectors,
    ChartSqlRenderers renderers,
    ISqlQueryValidator validator,
    OrganizationAccess access)
{
    public async Task<ChartResult> HandleAsync(Guid organizationId, Guid datasetId, ChartQuery query, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);

        var dataset = await datasets.GetAsync(organizationId, datasetId, ct)
            ?? throw new NotFoundException("Dataset not found.");
        var (source, connectionString) = await DataSourceLookup.LoadAsync(dataSources, secrets, organizationId, dataset.DataSourceId, ct);

        // Same rule as every other run: the stored SQL is validated again, in case it was changed outside the app.
        var datasetSql = validator.ValidateReadOnly(source.Type, dataset.Sql);
        var plan = ChartQueryPlanner.Plan(dataset.Columns, query ?? new ChartQuery());
        var built = renderers.Get(source.Type).Render(datasetSql, plan);

        var result = await connectors.Get(source.Type).ExecuteAsync(connectionString, built, plan.Limit, ct);

        return new ChartResult(
            result.Columns,
            result.Rows,
            result.Truncated,
            member.HasAtLeast(OrganizationRole.Editor) ? built.Sql : null);
    }
}
