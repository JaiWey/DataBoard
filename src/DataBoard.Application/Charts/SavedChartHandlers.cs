using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Dashboards;
using DataBoard.Application.Datasets;
using DataBoard.Application.Folders;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Charts;
using DataBoard.Domain.Folders;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Charts;

internal static class ChartMapping
{
    public static ChartDto ToDto(this Chart c) =>
        new(c.Id, c.Name, c.Description, c.DatasetId, c.Type, ChartDefinition.FromJson(c.DefinitionJson), c.CreatedUtc, c.UpdatedUtc, c.FolderId);
}

/// <summary>
/// Checks a chart before it is saved: its dataset exists in the organization, and its query plans cleanly against that
/// dataset's columns. A chart that could never run is rejected up front, with the reason.
/// </summary>
internal static class ChartDefinitionValidator
{
    public static readonly string[] Palettes = ["default", "warm", "cool", "mono"];

    /// <summary>Returns the normalised definition (query defaults filled in, style made complete).</summary>
    public static async Task<ChartDefinition> NormalizeAsync(
        SaveChartRequest request, IDatasetRepository datasets, Guid organizationId, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.Type))
            throw new ValidationException("Type", "Unknown chart type.");

        var dataset = await datasets.GetAsync(organizationId, request.DatasetId, ct)
            ?? throw new ValidationException("DatasetId", "The dataset was not found.");

        var query = request.Definition?.Query ?? new ChartQuery();
        ChartQueryPlanner.Plan(dataset.Columns, query); // throws a ValidationException that names the problem
        CheckShape(request.Type, query);
        // The values the chart gives the dataset's parameters have to fit them (and every parameter needs a value or a default).
        SqlParameters.Bind(dataset.Sql, DatasetParametersJson.FromJson(dataset.ParametersJson), query.Parameters);

        var style = request.Definition?.Style ?? new ChartStyle();
        if (!Palettes.Contains(style.Palette))
            throw new ValidationException("Style", $"Palette must be one of: {string.Join(", ", Palettes)}.");
        if (style.Percent && request.Type is not (ChartType.Bar or ChartType.BarHorizontal or ChartType.Line or ChartType.Area))
            throw new ValidationException("Style", "100% stacking is for bar, horizontal bar, line and area charts.");

        // 100% is a way of stacking.
        return new ChartDefinition(query, style.Percent ? style with { Stack = true } : style);
    }

    /// <summary>What the types that need a particular shape of result need. The others draw whatever they are given.</summary>
    private static void CheckShape(ChartType type, ChartQuery query)
    {
        var dimensions = query.Dimensions?.Count ?? 0;
        var measures = query.Measures?.Count ?? 0;

        switch (type)
        {
            case ChartType.Heatmap when dimensions != 2 || measures != 1:
                throw new ValidationException("Type", "A heatmap needs exactly two dimensions (across and down) and one measure (the colour).");
            case ChartType.Scatter when dimensions is < 1 or > 2 || measures is < 2 or > 3:
                throw new ValidationException("Type",
                    "A scatter chart needs one dimension (what each point is) and two measures (X and Y). " +
                    "A second dimension splits the points into coloured series, and a third measure sizes them.");
        }
    }
}

public sealed class CreateChartHandler(
    IChartRepository charts, IDatasetRepository datasets, IFolderRepository folders, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<ChartDto> HandleAsync(Guid organizationId, SaveChartRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();

        if (await charts.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, null, ct))
            throw new ConflictException("A chart with that name already exists.");

        await FolderRules.RequireFolderAsync(folders, organizationId, FolderKind.Chart, request.FolderId, "FolderId", ct);
        var definition = await ChartDefinitionValidator.NormalizeAsync(request, datasets, organizationId, ct);
        var chart = DatasetQuery.Guard(() => Chart.Create(
            organizationId, request.DatasetId, request.Name!, request.Description, request.Type, definition.ToJson(), userId, clock.UtcNow,
            request.FolderId));

        await charts.AddAsync(chart, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return chart.ToDto();
    }
}

public sealed class ListChartsHandler(IChartRepository charts, OrganizationAccess access)
{
    public async Task<IReadOnlyList<ChartDto>> HandleAsync(Guid organizationId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        return (await charts.ListByOrganizationAsync(organizationId, ct)).Select(c => c.ToDto()).ToList();
    }
}

public sealed class GetChartHandler(IChartRepository charts, OrganizationAccess access)
{
    public async Task<ChartDto> HandleAsync(Guid organizationId, Guid chartId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var chart = await charts.GetAsync(organizationId, chartId, ct) ?? throw new NotFoundException("Chart not found.");
        return chart.ToDto();
    }
}

public sealed class UpdateChartHandler(
    IChartRepository charts, IDatasetRepository datasets, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<ChartDto> HandleAsync(Guid organizationId, Guid chartId, SaveChartRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var chart = await charts.GetAsync(organizationId, chartId, ct) ?? throw new NotFoundException("Chart not found.");
        if (await charts.NameExistsAsync(organizationId, request.Name?.Trim() ?? string.Empty, chart.Id, ct))
            throw new ConflictException("A chart with that name already exists.");

        var definition = await ChartDefinitionValidator.NormalizeAsync(request, datasets, organizationId, ct);
        DatasetQuery.Guard(() =>
        {
            chart.Update(request.DatasetId, request.Name!, request.Description, request.Type, definition.ToJson(), clock.UtcNow);
            return true;
        });

        await unitOfWork.SaveChangesAsync(ct);
        return chart.ToDto();
    }
}

/// <summary>Deleting a chart also takes it off every dashboard that showed it, so no dashboard is left pointing at nothing.</summary>
public sealed class DeleteChartHandler(
    IChartRepository charts, IDashboardRepository dashboards, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task HandleAsync(Guid organizationId, Guid chartId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);

        var chart = await charts.GetAsync(organizationId, chartId, ct) ?? throw new NotFoundException("Chart not found.");

        foreach (var dashboard in await dashboards.ListByOrganizationAsync(organizationId, ct))
        {
            var definition = DashboardJson.Deserialize(dashboard.DefinitionJson);
            if (definition.Widgets.All(w => w.ChartId != chartId)) continue;

            // The list above is not tracked; load the tracked copy to change it.
            var tracked = await dashboards.GetAsync(organizationId, dashboard.Id, ct);
            if (tracked is null) continue;
            var pruned = definition with { Widgets = definition.Widgets.Where(w => w.ChartId != chartId).ToList() };
            tracked.Update(tracked.Name, tracked.Description, DashboardJson.Serialize(pruned), clock.UtcNow);
        }

        charts.Remove(chart);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// Runs a saved chart through the normal chart-query path (planner, validated SQL, read-only execution). Any member may
/// run it; only Editors and Owners get the generated SQL back.
/// </summary>
public sealed class RunSavedChartHandler(IChartRepository charts, RunChartQueryHandler runner, OrganizationAccess access)
{
    public async Task<ChartResult> HandleAsync(Guid organizationId, Guid chartId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var chart = await charts.GetAsync(organizationId, chartId, ct) ?? throw new NotFoundException("Chart not found.");
        var definition = ChartDefinition.FromJson(chart.DefinitionJson);
        return await runner.HandleAsync(organizationId, chart.DatasetId, definition.Query, CacheRequest.Use, ct);
    }
}
