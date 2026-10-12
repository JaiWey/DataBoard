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
