using DataBoard.Application.Abstractions;
using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.DataSources;
using DataBoard.Application.Folders;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Charts;
using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Folders;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Dashboards;

// ---- the file a dashboard is exported to ------------------------------------------------------------------------

/// <summary>
/// A dashboard as a file, to keep or to move to another organization. It names things instead of pointing at them: a
/// chart is its name (so the other organization needs a chart of that name), a tab is a key local to the file, and a
/// picture is bytes inside the file. No query, dataset or SQL is in it.
/// </summary>
/// <param name="Format">Always <see cref="DashboardPackages.Format"/>.</param>
/// <param name="Version">The version of this layout of the file; <see cref="DashboardPackages.CurrentVersion"/>.</param>
public sealed record DashboardPackage(
    string Format, int Version, string Name, string? Description,
    IReadOnlyList<PackageTab>? Tabs, IReadOnlyList<PackageFilter>? Filters, IReadOnlyList<PackageWidget>? Widgets,
    PackageBackground? Background, IReadOnlyDictionary<string, PackageFile>? Files, int? ReloadSeconds = null);

public sealed record PackageTab(string Key, string Name);

public sealed record PackageFilter(string Label, string Column, FilterOperator Operator, FilterTarget Target = FilterTarget.Column);

/// <param name="Chart">The name of the chart (chart widgets).</param>
/// <param name="Tab">The key of its tab; missing when the dashboard has no tabs.</param>
/// <param name="Image">The key of the picture among the package's files (picture widgets).</param>
public sealed record PackageWidget(
    WidgetKind Kind, string? Chart = null, string? Tab = null, WidgetLayout? Layout = null,
    string? Text = null, string? TextAlign = null, string? TextSize = null,
    string? Image = null, string? ImageFit = null, string? Alt = null, string? LinkUrl = null);

public sealed record PackageBackground(string Image, string Fit, int Opacity);

/// <param name="Data">The picture, in base64.</param>
public sealed record PackageFile(string ContentType, string Data);

public static class DashboardPackages
{
    public const string Format = "databoard-dashboard";
    public const int CurrentVersion = 1;

    /// <summary>Pictures in a file: every picture widget and the background, at most.</summary>
    public const int MaxFiles = DashboardValidator.MaxWidgets + 1;

    /// <summary>How big an imported file may be as a whole (the pictures are most of it), in bytes.</summary>
    public const long MaxRequestBytes = 64L * 1024 * 1024;

    /// <summary>
    /// How much picture an export may hold, as base64 characters: a little under what an import takes, so a file that was
    /// exported can always be imported.
    /// </summary>
    public const long MaxPictureCharacters = 60L * 1024 * 1024;
}

public sealed record ImportDashboardRequest(DashboardPackage? Package, string? Name = null, Guid? FolderId = null);

/// <param name="MissingCharts">Charts the file names that this organization does not have. Their widgets were left out.</param>
public sealed record ImportDashboardResult(DashboardDto Dashboard, IReadOnlyList<string> MissingCharts);

public sealed record DuplicateDashboardRequest(string? Name = null);

internal static class DashboardNames
{
    /// <summary>The name, or the name with " (2)", " (3)"... after it, whichever is not taken. Always within the length limit.</summary>
    public static async Task<string> UniqueAsync(IDashboardRepository dashboards, Guid organizationId, string wanted, CancellationToken ct)
    {
        var name = wanted.Trim();
        if (name.Length > Dashboard.MaxNameLength) name = name[..Dashboard.MaxNameLength].TrimEnd();
        if (!await dashboards.NameExistsAsync(organizationId, name, null, ct)) return name;

        for (var n = 2; n < 1000; n++)
        {
            var suffix = $" ({n})";
            var candidate = (name.Length + suffix.Length > Dashboard.MaxNameLength ? name[..(Dashboard.MaxNameLength - suffix.Length)].TrimEnd() : name) + suffix;
            if (!await dashboards.NameExistsAsync(organizationId, candidate, null, ct)) return candidate;
        }
        throw new ConflictException("A dashboard with that name already exists.");
    }
}

// ---- export -----------------------------------------------------------------------------------------------------

/// <summary>Writes a dashboard out as a <see cref="DashboardPackage"/>. Editors and Owners.</summary>
public sealed class ExportDashboardHandler(
    IDashboardRepository dashboards, IChartRepository charts, IImageStore images, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<DashboardPackage> HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        var definition = DashboardJson.Deserialize(dashboard.DefinitionJson);

        var chartNames = (await charts.ListByOrganizationAsync(organizationId, ct)).ToDictionary(c => c.Id, c => c.Name);
        var tabs = definition.Tabs ?? [];
        var tabKeys = tabs.Select((t, i) => (t.Id, Key: $"tab-{i + 1}")).ToDictionary(x => x.Id, x => x.Key);
        var firstTab = tabs.FirstOrDefault()?.Id;

        var files = new Dictionary<string, PackageFile>();
        long pictureCharacters = 0;
        var fileKeys = new Dictionary<string, string>(); // stored key -> key in the file
        string? Include(string? storedKey)
        {
            if (storedKey is null) return null;
            if (fileKeys.TryGetValue(storedKey, out var known)) return known;
            using var stream = images.Open(storedKey);
            if (stream is null) return null; // the file is gone; the widget goes without its picture
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            var key = $"image-{files.Count + 1}";
            var data = Convert.ToBase64String(buffer.ToArray());
            pictureCharacters += data.Length;
            if (pictureCharacters > DashboardPackages.MaxPictureCharacters)
                throw new ValidationException("Pictures", "The pictures on this dashboard are too big to export in one file.");
            files[key] = new PackageFile(ImageSniffer.ContentTypeOfKey(storedKey), data);
            fileKeys[storedKey] = key;
            return key;
        }

        var widgets = new List<PackageWidget>();
        foreach (var w in definition.Widgets)
        {
            var tab = tabs.Count == 0 ? null : tabKeys[tabs.Any(t => t.Id == w.TabId) ? w.TabId!.Value : firstTab!.Value];
            switch (w.Kind)
            {
                case WidgetKind.Chart:
                    if (chartNames.TryGetValue(w.ChartId, out var name))
                        widgets.Add(new PackageWidget(WidgetKind.Chart, Chart: name, Tab: tab, Layout: w.Layout));
                    break; // a deleted chart is not part of the dashboard any more
                case WidgetKind.Text:
                    widgets.Add(new PackageWidget(WidgetKind.Text, Tab: tab, Layout: w.Layout, Text: w.Text, TextAlign: w.TextAlign, TextSize: w.TextSize));
                    break;
                case WidgetKind.Image:
                    if (Include(w.ImageKey) is { } image)
                        widgets.Add(new PackageWidget(WidgetKind.Image, Tab: tab, Layout: w.Layout, Image: image, ImageFit: w.ImageFit, Alt: w.Alt, LinkUrl: w.LinkUrl));
                    break;
            }
        }

        var background = Include(dashboard.BackgroundImageKey) is { } backgroundImage
            ? new PackageBackground(backgroundImage, dashboard.BackgroundFit, dashboard.BackgroundOpacity)
            : null;

        return new DashboardPackage(
            DashboardPackages.Format, DashboardPackages.CurrentVersion, dashboard.Name, dashboard.Description,
            tabs.Count == 0 ? null : tabs.Select(t => new PackageTab(tabKeys[t.Id], t.Name)).ToList(),
            definition.Filters.Select(f => new PackageFilter(f.Label, f.Column, f.Operator, f.Target)).ToList(),
            widgets, background, files.Count == 0 ? null : files, definition.ReloadSeconds);
    }
}

// ---- import -----------------------------------------------------------------------------------------------------

/// <summary>
/// Makes a dashboard from a <see cref="DashboardPackage"/>. Charts are found by name; those the organization does not have
/// are left out and reported. Everything in the file is checked as if it had been typed in: the pictures by their first
/// bytes, the widgets and filters by the same rules as any dashboard. Editors and Owners.
/// </summary>
public sealed class ImportDashboardHandler(
    IDashboardRepository dashboards, IChartRepository charts, IFolderRepository folders, IImageStore images,
    IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<ImportDashboardResult> HandleAsync(Guid organizationId, ImportDashboardRequest request, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();
        var package = request.Package ?? throw new ValidationException("Package", "Choose a dashboard file to import.");
        if (package.Format != DashboardPackages.Format)
            throw new ValidationException("Package", "That is not a DataBoard dashboard file.");
        if (package.Version != DashboardPackages.CurrentVersion)
            throw new ValidationException("Package", $"This file is version {package.Version}; this DataBoard reads version {DashboardPackages.CurrentVersion}.");
        await FolderRules.RequireFolderAsync(folders, organizationId, FolderKind.Dashboard, request.FolderId, "FolderId", ct);
        await visibility.RequireFolderAsync(member, organizationId, request.FolderId, "FolderId", ct);

        var wantedName = string.IsNullOrWhiteSpace(request.Name) ? package.Name : request.Name;
        if (string.IsNullOrWhiteSpace(wantedName)) throw new ValidationException("Name", "The dashboard needs a name.");

        var saved = new List<string>(); // pictures kept so far, which are thrown away if the import fails
        try
        {
            var files = package.Files ?? new Dictionary<string, PackageFile>();
            if (files.Count > DashboardPackages.MaxFiles)
                throw new ValidationException("Package", "The file holds too many pictures.");
            var stored = new Dictionary<string, string>(); // key in the file -> stored key
            async Task<string> Keep(string fileKey, string field)
            {
                if (stored.TryGetValue(fileKey, out var known)) return known;
                if (!files.TryGetValue(fileKey, out var file) || file is null)
                    throw new ValidationException(field, "The file names a picture that is not in it.");
                var bytes = Decode(file.Data, field);
                var kind = ImageSniffer.Detect(bytes) ?? throw new ValidationException(field, "A picture in the file is not a PNG, JPEG, GIF or WebP.");
                var key = await images.SaveAsync(bytes, kind.Extension, ct);
                saved.Add(key);
                stored[fileKey] = key;
                return key;
            }

            // Tabs: the file's keys become new ids.
            var tabIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
            var tabs = new List<DashboardTab>();
            foreach (var tab in package.Tabs ?? [])
            {
                if (tab is null || string.IsNullOrWhiteSpace(tab.Key) || !tabIds.TryAdd(tab.Key, Guid.NewGuid()))
                    throw new ValidationException("Package", "The tabs in the file are not valid.");
                tabs.Add(new DashboardTab(tabIds[tab.Key], tab.Name));
            }

            // Widgets: charts by name, pictures from the file; whatever cannot be found is left out and reported.
            var chartsByName = (await charts.ListByOrganizationAsync(organizationId, ct))
                .GroupBy(c => c.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Id);
            var missing = new List<string>();
            var widgets = new List<DashboardWidget>();
            var pictures = new Dictionary<Guid, DashboardWidget>();
            var wanted = package.Widgets ?? [];
            for (var i = 0; i < wanted.Count; i++)
            {
                var w = wanted[i] ?? throw new ValidationException($"Widgets[{i}]", "A widget in the file is empty.");
                Guid? tab = null;
                if (w.Tab is not null)
                {
                    if (!tabIds.TryGetValue(w.Tab, out var id)) throw new ValidationException($"Widgets[{i}]", "A widget names a tab that is not in the file.");
                    tab = id;
                }

                switch (w.Kind)
                {
                    case WidgetKind.Chart:
                        if (string.IsNullOrWhiteSpace(w.Chart)) throw new ValidationException($"Widgets[{i}]", "A chart widget needs the name of a chart.");
                        if (chartsByName.TryGetValue(w.Chart.Trim().ToLowerInvariant(), out var chartId))
                            widgets.Add(new DashboardWidget(Guid.Empty, chartId, w.Layout, TabId: tab));
                        else if (!missing.Contains(w.Chart.Trim(), StringComparer.OrdinalIgnoreCase))
                            missing.Add(w.Chart.Trim());
                        break;

                    case WidgetKind.Text:
                        widgets.Add(new DashboardWidget(Guid.Empty, Guid.Empty, w.Layout, WidgetKind.Text, Text: w.Text, TextAlign: w.TextAlign, TextSize: w.TextSize, TabId: tab));
                        break;

                    case WidgetKind.Image:
                        if (string.IsNullOrWhiteSpace(w.Image)) throw new ValidationException($"Widgets[{i}]", "A picture widget needs a picture.");
                        var key = await Keep(w.Image, $"Widgets[{i}]");
                        var picture = new DashboardWidget(
                            Guid.NewGuid(), Guid.Empty, w.Layout, WidgetKind.Image, key, w.ImageFit ?? "contain", w.Alt, w.LinkUrl, TabId: tab);
                        pictures[picture.Id] = picture;
                        widgets.Add(picture);
                        break;

                    default:
                        throw new ValidationException($"Widgets[{i}]", "A widget in the file is of a kind this DataBoard does not know.");
                }
            }

            var filters = (package.Filters ?? []).Select(f => new DashboardFilter(Guid.Empty, f.Label, f.Column, f.Operator, f.Target)).ToList();

            // The same checks as any dashboard; the pictures are "existing" widgets, as the server just made them.
            var definition = await DashboardValidator.NormalizeAsync(
                new DashboardDefinition(widgets, filters, tabs.Count > 0 ? tabs : null, package.ReloadSeconds), charts, organizationId, pictures, ct);

            string? backgroundKey = null;
            if (package.Background is { } background)
                backgroundKey = await Keep(background.Image, "Background");

            var name = await DashboardNames.UniqueAsync(dashboards, organizationId, wantedName, ct);
            var dashboard = DatasetQuery.Guard(() =>
            {
                var d = Dashboard.Create(organizationId, name, package.Description, DashboardJson.Serialize(definition), userId, clock.UtcNow, request.FolderId);
                if (backgroundKey is not null)
                    d.SetBackground(backgroundKey, package.Background!.Fit, package.Background.Opacity);
                return d;
            });

            await dashboards.AddAsync(dashboard, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return new ImportDashboardResult(dashboard.ToDto(), missing);
        }
        catch
        {
            foreach (var key in saved) images.Delete(key);
            throw;
        }
    }

    private static byte[] Decode(string data, string field)
    {
        if (string.IsNullOrEmpty(data)) throw new ValidationException(field, "A picture in the file is empty.");
        // Bound the size before decoding: base64 is 4 characters for 3 bytes.
        if (data.Length > ImageLimits.MaxBytes * 4 / 3 + 8) throw new ValidationException(field, "A picture in the file is too big.");
        try
        {
            var bytes = Convert.FromBase64String(data);
            if (bytes.Length > ImageLimits.MaxBytes) throw new ValidationException(field, "A picture in the file is too big.");
            return bytes;
        }
        catch (FormatException)
        {
            throw new ValidationException(field, "A picture in the file is not valid.");
        }
    }
}

// ---- duplicate --------------------------------------------------------------------------------------------------

/// <summary>
/// Copies a dashboard in the same organization: its tabs, widgets (the charts stay shared), shared filters, pictures and
/// background. The copy starts with no share links and no refresh schedule, and is filed in the same folder. Editors and Owners.
/// </summary>
public sealed class DuplicateDashboardHandler(
    IDashboardRepository dashboards, IImageStore images, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, DuplicateDashboardRequest? request, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();
        var source = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        var definition = DashboardJson.Deserialize(source.DefinitionJson);

        var copies = new List<string>(); // pictures copied so far, which are thrown away if saving fails
        try
        {
            async Task<string?> Copy(string? key)
            {
                if (key is null) return null;
                using var stream = images.Open(key);
                if (stream is null) return null; // the file is gone: the copy goes without it
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                var copy = await images.SaveAsync(buffer.ToArray(), Path.GetExtension(key).TrimStart('.'), ct);
                copies.Add(copy);
                return copy;
            }

            var tabIds = (definition.Tabs ?? []).ToDictionary(t => t.Id, _ => Guid.NewGuid());
            var widgets = new List<DashboardWidget>();
            foreach (var w in definition.Widgets)
            {
                var copy = w with { Id = Guid.NewGuid(), TabId = w.TabId is { } t && tabIds.TryGetValue(t, out var mapped) ? mapped : null };
                if (w.Kind == WidgetKind.Image)
                {
                    var key = await Copy(w.ImageKey);
                    if (key is null) continue;
                    copy = copy with { ImageKey = key };
                }
                widgets.Add(copy);
            }

            var copied = new DashboardDefinition(
                widgets,
                definition.Filters.Select(f => f with { Id = Guid.NewGuid() }).ToList(),
                definition.Tabs?.Select(t => t with { Id = tabIds[t.Id] }).ToList() is { Count: > 0 } tabs ? tabs : null,
                definition.ReloadSeconds);

            var wanted = string.IsNullOrWhiteSpace(request?.Name) ? $"Copy of {source.Name}" : request!.Name!;
            var name = await DashboardNames.UniqueAsync(dashboards, organizationId, wanted, ct);
            var backgroundKey = await Copy(source.BackgroundImageKey);

            var dashboard = DatasetQuery.Guard(() =>
            {
                var d = Dashboard.Create(organizationId, name, source.Description, DashboardJson.Serialize(copied), userId, clock.UtcNow, source.FolderId);
                if (backgroundKey is not null) d.SetBackground(backgroundKey, source.BackgroundFit, source.BackgroundOpacity);
                d.SetAccess(source.Access); // a copy of a limited dashboard is limited the same way
                return d;
            });

            await dashboards.AddAsync(dashboard, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return dashboard.ToDto();
        }
        catch
        {
            foreach (var key in copies) images.Delete(key);
            throw;
        }
    }
}

// ---- copy to another organization ---------------------------------------------------------------------------------

/// <param name="NewDatasets">Datasets made in the other organization (the rest were found there by name).</param>
/// <param name="NewCharts">Charts made in the other organization (the rest were found there by name).</param>
/// <param name="MissingDataSources">Data sources the dashboard needs that the other organization does not have, by name.</param>
/// <param name="SkippedCharts">Charts left out (with their widgets) because their data source is missing there.</param>
public sealed record CopyDashboardResult(
    DashboardDto Dashboard, int NewDatasets, int NewCharts, IReadOnlyList<string> MissingDataSources, IReadOnlyList<string> SkippedCharts);

/// <summary>
/// Copies a dashboard into another organization the person belongs to, with the charts it shows and the datasets they use.
/// Data sources are not copied (their connection details stay where they are): each dataset is made on the data source of the
/// same name in the other organization, and where there is none the dataset, its charts and their widgets are left out and
/// reported. A dataset or chart whose name is already taken there is used as it is, not made again. Everything is saved at
/// once, so a copy is whole or not there. Editors and Owners in both organizations.
/// </summary>
public sealed class CopyDashboardToOrganizationHandler(
    IDashboardRepository dashboards, IChartRepository charts, IDatasetRepository datasets, IDataSourceRepository dataSources,
    IImageStore images, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<CopyDashboardResult> HandleAsync(Guid organizationId, Guid dashboardId, Guid targetOrganizationId, CancellationToken ct = default)
    {
        if (targetOrganizationId == organizationId)
            throw new ValidationException("TargetOrganizationId", "Choose another organization. Use Duplicate to copy within this one.");

        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        await access.RequireRoleAsync(targetOrganizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();
        var now = clock.UtcNow;

        var source = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        var definition = DashboardJson.Deserialize(source.DefinitionJson);

        // What the dashboard shows: its charts, their datasets and those datasets' data sources.
        var chartIds = definition.Widgets.Where(w => w.Kind == WidgetKind.Chart).Select(w => w.ChartId).ToHashSet();
        var sourceCharts = (await charts.ListByOrganizationAsync(organizationId, ct)).Where(c => chartIds.Contains(c.Id)).ToDictionary(c => c.Id);
        var sourceDatasets = (await datasets.ListByOrganizationAsync(organizationId, ct)).ToDictionary(d => d.Id);
        var sourceSources = (await dataSources.ListByOrganizationAsync(organizationId, ct)).ToDictionary(s => s.Id, s => s.Name);

        var targetSources = (await dataSources.ListByOrganizationAsync(targetOrganizationId, ct))
            .GroupBy(s => s.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Id);
        var targetDatasets = (await datasets.ListByOrganizationAsync(targetOrganizationId, ct))
            .GroupBy(d => d.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Id);
        var targetCharts = (await charts.ListByOrganizationAsync(targetOrganizationId, ct))
            .GroupBy(c => c.Name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First().Id);

        var missingSources = new List<string>();
        var skippedCharts = new List<string>();
        var datasetMap = new Dictionary<Guid, Guid?>(); // source dataset -> its dataset in the target (null: cannot be made there)
        var chartMap = new Dictionary<Guid, Guid>();    // source chart -> its chart in the target
        var newDatasets = 0;
        var newCharts = 0;

        var copies = new List<string>(); // pictures copied so far, which are thrown away if saving fails
        try
        {
            foreach (var chart in sourceCharts.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (targetCharts.TryGetValue(chart.Name.ToLowerInvariant(), out var existingChart))
                {
                    chartMap[chart.Id] = existingChart;
                    continue;
                }

                if (!datasetMap.TryGetValue(chart.DatasetId, out var targetDataset))
                {
                    targetDataset = null;
                    if (sourceDatasets.TryGetValue(chart.DatasetId, out var dataset))
                    {
                        if (targetDatasets.TryGetValue(dataset.Name.ToLowerInvariant(), out var existingDataset))
                        {
                            targetDataset = existingDataset;
                        }
                        else if (sourceSources.TryGetValue(dataset.DataSourceId, out var sourceName)
                                 && targetSources.TryGetValue(sourceName.ToLowerInvariant(), out var targetSource))
                        {
                            var made = DatasetQuery.Guard(() =>
                            {
                                var d = Dataset.Create(
                                    targetOrganizationId, targetSource, dataset.Name, dataset.Description, dataset.Sql, dataset.Columns,
                                    userId, now, dataset.Kind, dataset.DefinitionJson, dataset.ParametersJson);
                                d.SetCacheSeconds(dataset.CacheSeconds);
                                return d;
                            });
                            await datasets.AddAsync(made, ct);
                            targetDatasets[dataset.Name.ToLowerInvariant()] = made.Id;
                            targetDataset = made.Id;
                            newDatasets++;
                        }
                        else if (sourceSources.TryGetValue(dataset.DataSourceId, out var missing)
                                 && !missingSources.Contains(missing, StringComparer.OrdinalIgnoreCase))
                        {
                            missingSources.Add(missing);
                        }
                    }
                    datasetMap[chart.DatasetId] = targetDataset;
                }

                if (targetDataset is not { } datasetId)
                {
                    skippedCharts.Add(chart.Name);
                    continue;
                }

                var copy = DatasetQuery.Guard(() =>
                    Chart.Create(targetOrganizationId, datasetId, chart.Name, chart.Description, chart.Type, chart.DefinitionJson, userId, now));
                await charts.AddAsync(copy, ct);
                targetCharts[chart.Name.ToLowerInvariant()] = copy.Id;
                chartMap[chart.Id] = copy.Id;
                newCharts++;
            }

            async Task<string?> Copy(string? key)
            {
                if (key is null) return null;
                using var stream = images.Open(key);
                if (stream is null) return null; // the file is gone: the copy goes without it
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, ct);
                var copy = await images.SaveAsync(buffer.ToArray(), Path.GetExtension(key).TrimStart('.'), ct);
                copies.Add(copy);
                return copy;
            }

            var tabIds = (definition.Tabs ?? []).ToDictionary(t => t.Id, _ => Guid.NewGuid());
            var widgets = new List<DashboardWidget>();
            foreach (var w in definition.Widgets)
            {
                var widget = w with { Id = Guid.NewGuid(), TabId = w.TabId is { } t && tabIds.TryGetValue(t, out var mapped) ? mapped : null };
                switch (w.Kind)
                {
                    case WidgetKind.Chart:
                        if (!chartMap.TryGetValue(w.ChartId, out var chartId)) continue; // skipped, or deleted in the source
                        widget = widget with { ChartId = chartId };
                        break;
                    case WidgetKind.Image:
                        var key = await Copy(w.ImageKey);
                        if (key is null) continue;
                        widget = widget with { ImageKey = key };
                        break;
                }
                widgets.Add(widget);
            }

            var copied = new DashboardDefinition(
                widgets,
                definition.Filters.Select(f => f with { Id = Guid.NewGuid() }).ToList(),
                definition.Tabs?.Select(t => t with { Id = tabIds[t.Id] }).ToList() is { Count: > 0 } tabs ? tabs : null,
                definition.ReloadSeconds);

            var name = await DashboardNames.UniqueAsync(dashboards, targetOrganizationId, source.Name, ct);
            var backgroundKey = await Copy(source.BackgroundImageKey);

            var dashboard = DatasetQuery.Guard(() =>
            {
                var d = Dashboard.Create(targetOrganizationId, name, source.Description, DashboardJson.Serialize(copied), userId, now);
                if (backgroundKey is not null) d.SetBackground(backgroundKey, source.BackgroundFit, source.BackgroundOpacity);
                return d;
            });

            await dashboards.AddAsync(dashboard, ct);
            await unitOfWork.SaveChangesAsync(ct);
            return new CopyDashboardResult(dashboard.ToDto(), newDatasets, newCharts, missingSources, skippedCharts);
        }
        catch
        {
            foreach (var key in copies) images.Delete(key);
            throw;
        }
    }
}
