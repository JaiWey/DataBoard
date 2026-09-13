using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.DataSources;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Charts;

/// <summary>
/// Runs a chart query over a dataset. Any member may run it (that is what dashboards do), but only Editors and
/// Owners get the generated SQL back, because it contains the dataset's SQL. Results can be reused from the cache
/// when the dataset keeps them (see <see cref="CacheRequest"/>); the answer says when a result was stored.
/// </summary>
public sealed class RunChartQueryHandler(
    IDatasetRepository datasets,
    IDataSourceRepository dataSources,
    ISecretProtector secrets,
    DataSourceConnectors connectors,
    ChartSqlRenderers renderers,
    ISqlQueryValidator validator,
    IResultCache cache,
    IClock clock,
    OrganizationAccess access)
{
    /// <summary>A live run: always asks the database and does not touch the cache.</summary>
    public Task<ChartResult> HandleAsync(Guid organizationId, Guid datasetId, ChartQuery query, CancellationToken ct = default) =>
        HandleAsync(organizationId, datasetId, query, CacheRequest.Live, ct);

    public async Task<ChartResult> HandleAsync(
        Guid organizationId, Guid datasetId, ChartQuery query, CacheRequest cacheRequest, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        return await ExecuteAsync(organizationId, datasetId, query, member.HasAtLeast(OrganizationRole.Editor), cacheRequest, ct);
    }

    /// <summary>
    /// For a caller that has already been authorised some other way (a public dashboard link, whose token was checked, or
    /// the scheduled refresh). No organization membership is needed, and the generated SQL is never returned.
    /// </summary>
    public Task<ChartResult> HandleTrustedAsync(
        Guid organizationId, Guid datasetId, ChartQuery query, CacheRequest cacheRequest, CancellationToken ct = default) =>
        ExecuteAsync(organizationId, datasetId, query, includeSql: false, cacheRequest, ct);

    private async Task<ChartResult> ExecuteAsync(
        Guid organizationId, Guid datasetId, ChartQuery query, bool includeSql, CacheRequest cacheRequest, CancellationToken ct)
    {
        var dataset = await datasets.GetAsync(organizationId, datasetId, ct)
            ?? throw new NotFoundException("Dataset not found.");
        var (source, connectionString) = await DataSourceLookup.LoadAsync(dataSources, secrets, organizationId, dataset.DataSourceId, ct);

        // The dataset's {{parameters}} become bound values (the chart's own, else the defaults) before the SQL is checked.
        var bound = SqlParameters.Bind(dataset.Sql, DatasetParametersJson.FromJson(dataset.ParametersJson), query?.Parameters);

        // Same rule as every other run: the stored SQL is validated again, in case it was changed outside the app.
        var datasetSql = validator.ValidateReadOnly(source.Type, bound.Sql);
        var plan = ChartQueryPlanner.Plan(dataset.Columns, query ?? new ChartQuery());
        var built = renderers.Get(source.Type).Render(datasetSql, plan, bound.Parameters);

        var key = cacheRequest.Mode == CacheMode.Live
            ? null
            : ResultCacheKey.For(
                organizationId, dataset.Id, dataset.UpdatedUtc, dataset.CacheSeconds, source.Id, source.EncryptedConnectionString, built, plan.Limit);

        if (key is not null && cacheRequest.Mode == CacheMode.Use && cache.Get(key) is { } hit)
            return new ChartResult(hit.Result.Columns, hit.Result.Rows, hit.Result.Truncated, includeSql ? hit.Sql : null, hit.CachedUtc);

        var result = await connectors.Get(source.Type).ExecuteAsync(connectionString, built, plan.Limit, ct);

        if (key is not null)
        {
            // A refresh keeps the result as long as the dataset does, or longer if the caller (a schedule) asks.
            var keep = TimeSpan.FromSeconds(dataset.CacheSeconds);
            if (cacheRequest is { Mode: CacheMode.Refresh, StoreFor: { } more } && more > keep) keep = more;
            if (keep > TimeSpan.Zero) cache.Set(key, new CachedResult(result, built.Sql, clock.UtcNow), keep);
        }

        return new ChartResult(result.Columns, result.Rows, result.Truncated, includeSql ? built.Sql : null);
    }
}
