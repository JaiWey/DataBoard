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

        CheckStyle(style);

        // 100% is a way of stacking.
        return new ChartDefinition(query, style.Percent ? style with { Stack = true } : style);
    }

    public static readonly string[] LegendPositions = ["top", "bottom", "left", "right"];
    public static readonly string[] PieLabels = ["name", "value", "percent", "name-value", "name-percent"];
    public const int MaxColors = 12;
    public const int MaxTitleLength = 100;
    public const int MaxAxisTitleLength = 60;
    public const int MaxAffixLength = 12;

    public static readonly string[] FontFamilies = ["sans-serif", "serif", "monospace", "Arial", "Georgia", "Verdana", "Courier New"];
    public static readonly string[] FontWeights = ["normal", "bold"];
    public static readonly string[] LineTypes = ["solid", "dashed", "dotted"];
    public static readonly string[] LabelPositions = ["auto", "top", "bottom", "left", "right", "inside", "insideTop", "insideBottom", "outside", "center"];
    public static readonly string[] MarginUnits = ["px", "%"];
    public static readonly string[] TableSizes = ["small", "medium", "large"];
    public static readonly string[] TableAligns = ["left", "center", "right"];
    public static readonly string[] LegendTypes = ["auto", "scroll", "plain"];

    private static void CheckTable(TableStyle? table, Action<string> fail)
    {
        if (table is null) return;
        if (!TableSizes.Contains(table.Size)) fail($"A table is: {string.Join(", ", TableSizes)}.");
        if (!TableAligns.Contains(table.HeaderAlign)) fail($"A table header is aligned: {string.Join(", ", TableAligns)}.");
        if (table.PageSize is < 5 or > 500) fail("A table page is 5 to 500 rows.");
        CheckColor(table.HeaderBackground, "The header colour", fail);
        CheckColor(table.OddRowColor, "The odd row text colour", fail);
        CheckColor(table.OddRowBackground, "The odd row background", fail);
        CheckColor(table.EvenRowColor, "The even row text colour", fail);
        CheckColor(table.EvenRowBackground, "The even row background", fail);
        CheckFont(table.HeaderFont, "The header font", fail);
        CheckFont(table.BodyFont, "The table font", fail);
    }

    private static void CheckColor(string? color, string what, Action<string> fail)
    {
        if (color is not null && !HexColor.IsMatch(color)) fail($"{what} is written like #1971c2 or #19c.");
    }

    private static void CheckFont(FontStyle? font, string what, Action<string> fail)
    {
        if (font is null) return;
        if (font.Family is not null && !FontFamilies.Contains(font.Family)) fail($"{what} is one of: {string.Join(", ", FontFamilies)}.");
        if (font.Size is < 8 or > 48) fail($"{what} is 8 to 48 pixels.");
        if (font.Weight is not null && !FontWeights.Contains(font.Weight)) fail($"{what} is normal or bold.");
        CheckColor(font.Color, $"{what} colour", fail);
    }

    private static void CheckAxis(AxisStyle? axis, string what, Action<string> fail)
    {
        if (axis is null) return;
        CheckColor(axis.LineColor, $"{what} line colour", fail);
        CheckFont(axis.LabelFont, $"{what} label font", fail);
        CheckFont(axis.TitleFont, $"{what} title font", fail);
    }

    private static void CheckSplitLine(SplitLineStyle? line, string what, Action<string> fail)
    {
        if (line is null) return;
        CheckColor(line.Color, $"{what} colour", fail);
        if (line.Width is < 1 or > 10) fail($"{what} are 1 to 10 thick.");
        if (line.LineType is not null && !LineTypes.Contains(line.LineType)) fail($"{what} are: {string.Join(", ", LineTypes)}.");
    }

    private static readonly System.Text.RegularExpressions.Regex HexColor = new("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$");

    /// <summary>Ranges and shapes of the look choices. Anything outside them is refused with the field's name, so nothing odd is stored.</summary>
    private static void CheckStyle(ChartStyle style)
    {
        void Fail(string message) => throw new ValidationException("Style", message);

        if (style.Title is { Length: > MaxTitleLength }) Fail($"The title can be at most {MaxTitleLength} characters.");
        if (style.XAxisTitle is { Length: > MaxAxisTitleLength } || style.YAxisTitle is { Length: > MaxAxisTitleLength })
            Fail($"An axis title can be at most {MaxAxisTitleLength} characters.");
        if (style.YMin is { } min && !double.IsFinite(min) || style.YMax is { } max0 && !double.IsFinite(max0))
            Fail("The axis limits must be numbers.");
        if (style.YMin is { } lo && style.YMax is { } hi && lo >= hi) Fail("The axis minimum must be below the maximum.");
        if (style.LabelRotate is < -90 or > 90) Fail("Labels can be turned between -90 and 90 degrees.");

        if (style.Colors is { } colors)
        {
            if (colors.Count > MaxColors) Fail($"Use at most {MaxColors} colours.");
            if (colors.Any(c => c is null || !HexColor.IsMatch(c))) Fail("Colours are written like #1971c2 or #19c.");
        }
        if (style.SeriesColors is { } bySeries)
        {
            if (bySeries.Count > MaxColors * 4) Fail("Too many series colours.");
            if (bySeries.Any(kv => string.IsNullOrWhiteSpace(kv.Key) || kv.Key.Length > 200 || kv.Value is null || !HexColor.IsMatch(kv.Value)))
                Fail("A series colour needs the series' name and a colour like #1971c2.");
        }

        if (style.NumberFormat is { } format)
        {
            if (format.Decimals is < 0 or > 6) Fail("Decimals can be 0 to 6.");
            if (format.Prefix is { Length: > MaxAffixLength } || format.Suffix is { Length: > MaxAffixLength })
                Fail($"A prefix or suffix can be at most {MaxAffixLength} characters.");
        }

        if (!LegendPositions.Contains(style.LegendPosition)) Fail($"The legend goes at: {string.Join(", ", LegendPositions)}.");
        if (style.LineWidth is < 1 or > 8) Fail("Lines are 1 to 8 thick.");
        if (style.BarWidth is < 10 or > 100) Fail("A bar is 10 to 100 percent of its slot wide.");
        if (style.BarRadius is < 0 or > 20) Fail("Bar corners are rounded 0 to 20.");
        if (style.AreaOpacity is < 0 or > 100) Fail("An area is 0 to 100 percent solid.");
        if (style.InnerRadius is < 0 or > 90) Fail("The hole of a donut is 0 to 90 percent.");
        if (!PieLabels.Contains(style.PieLabel)) Fail($"A slice label is one of: {string.Join(", ", PieLabels)}.");

        if (!LineTypes.Contains(style.BarBorderStyle)) Fail($"A bar border is: {string.Join(", ", LineTypes)}.");
        if (style.BarBorderWidth is < 0 or > 10) Fail("A bar border is 0 to 10 thick.");
        CheckColor(style.BarBorderColor, "The bar border colour", Fail);
        if (style.BarGap is < 0 or > 100) Fail("The gap between bars is 0 to 100 percent.");
        if (style.BarCategoryGap is < 0 or > 100) Fail("The gap between groups of bars is 0 to 100 percent.");
        if (!LabelPositions.Contains(style.LabelPosition)) Fail($"A label goes at: {string.Join(", ", LabelPositions)}.");
        CheckFont(style.LabelFont, "The label font", Fail);
        if (!LegendTypes.Contains(style.LegendType)) Fail($"The legend is: {string.Join(", ", LegendTypes)}.");
        if (style.LegendHeight is < 20 or > 1000) Fail("The legend is 20 to 1000 pixels high.");
        CheckFont(style.LegendFont, "The legend font", Fail);
        CheckAxis(style.XAxis, "The horizontal axis", Fail);
        CheckAxis(style.YAxis, "The vertical axis", Fail);
        CheckSplitLine(style.XSplitLine, "The horizontal axis lines", Fail);
        CheckSplitLine(style.YSplitLine, "The vertical axis lines", Fail);
        CheckTable(style.Table, Fail);

        if (style.Margin is { } margin)
        {
            if (!MarginUnits.Contains(margin.Unit)) Fail($"The space around the plot is in: {string.Join(", ", MarginUnits)}.");
            var most = margin.Unit == "%" ? 50 : 300;
            if (new[] { margin.Left, margin.Top, margin.Right, margin.Bottom }.Any(v => v < 0 || v > most))
                Fail($"The space around the plot is 0 to {most} {margin.Unit}.");
        }
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
