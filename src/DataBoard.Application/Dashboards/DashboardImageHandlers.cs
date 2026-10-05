using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Dashboards;

/// <param name="Fit">contain, cover or fill. Left out, contain.</param>
/// <param name="Alt">What the picture shows, for people who cannot see it.</param>
/// <param name="LinkUrl">Where a click goes: an http or https address.</param>
public sealed record ImageWidgetOptions(string? Fit = null, string? Alt = null, string? LinkUrl = null);

internal static class ImageWidgets
{
    /// <summary>Checks what can be said about a picture widget; returns it cleaned (blank becomes null).</summary>
    public static ImageWidgetOptions Check(ImageWidgetOptions? options, ImageWidgetOptions? fallback = null)
    {
        var fit = options?.Fit ?? fallback?.Fit ?? "contain";
        if (!DashboardValidator.ImageFits.Contains(fit))
            throw new ValidationException("Fit", $"A picture is shown as: {string.Join(", ", DashboardValidator.ImageFits)}.");

        var alt = options?.Alt ?? fallback?.Alt;
        if (alt is { Length: > DashboardValidator.MaxAltLength })
            throw new ValidationException("Alt", $"The description of a picture can be at most {DashboardValidator.MaxAltLength} characters.");

        var link = options?.LinkUrl ?? fallback?.LinkUrl;
        if (!string.IsNullOrWhiteSpace(link) && !DashboardValidator.IsWebAddress(link))
            throw new ValidationException("LinkUrl", "A link must be an address starting with http:// or https://.");

        return new ImageWidgetOptions(fit, string.IsNullOrWhiteSpace(alt) ? null : alt.Trim(), string.IsNullOrWhiteSpace(link) ? null : link.Trim());
    }

    public static DashboardWidget Find(DashboardDefinition definition, Guid widgetId) =>
        definition.Widgets.FirstOrDefault(w => w.Id == widgetId && w.Kind == WidgetKind.Image && w.ImageKey is not null)
        ?? throw new NotFoundException("Picture not found.");
}

/// <summary>
/// Puts a picture on a dashboard as a widget of its own, at the bottom of the grid. The server makes the widget and names
/// the picture; its fit, description and link can be changed afterwards with the rest of the dashboard. Editors and Owners.
/// </summary>
public sealed class AddImageWidgetHandler(
    IDashboardRepository dashboards, IImageStore images, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access, DashboardVisibility visibility)
{
    public const int DefaultWidth = 4;
    public const int DefaultHeight = 4;

    public async Task<DashboardDto> HandleAsync(
        Guid organizationId, Guid dashboardId, Stream image, ImageWidgetOptions? options, Guid? tabId = null, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);

        var definition = DashboardJson.Deserialize(dashboard.DefinitionJson);
        if (definition.Widgets.Count >= DashboardValidator.MaxWidgets)
            throw new ValidationException("Widgets", $"A dashboard can have at most {DashboardValidator.MaxWidgets} widgets.");

        var checkedOptions = ImageWidgets.Check(options);
        var bytes = await ImageUpload.ReadAsync(image, "Choose a picture to put on the dashboard.", ct);
        var kind = ImageUpload.Kind(bytes);
        var key = await images.SaveAsync(bytes, kind.Extension, ct);

        // On the tab asked for (the first, if none or an unknown one), below everything there, at the left edge. If an older
        // widget there has no place yet, the client places this one with the rest.
        var tabs = definition.Tabs ?? [];
        Guid? tab = tabs.Count == 0 ? null : tabs.Any(t => t.Id == tabId) ? tabId : tabs[0].Id;
        var onTab = definition.Widgets.Where(w => (w.TabId ?? tabs.FirstOrDefault()?.Id) == tab).ToList();
        WidgetLayout? layout = onTab.All(w => w.Layout is not null)
            ? new WidgetLayout(0, onTab.Select(w => w.Layout!.Y + w.Layout.H).DefaultIfEmpty(0).Max(), DefaultWidth, DefaultHeight)
            : null;
        var widget = new DashboardWidget(
            Guid.NewGuid(), Guid.Empty, layout, WidgetKind.Image, key, checkedOptions.Fit, checkedOptions.Alt, checkedOptions.LinkUrl, TabId: tab);

        try
        {
            DatasetQuery.Guard(() =>
            {
                dashboard.Update(dashboard.Name, dashboard.Description, DashboardJson.Serialize(definition with { Widgets = [.. definition.Widgets, widget] }), clock.UtcNow);
                return true;
            });
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            images.Delete(key); // nothing was saved, so the picture is not wanted
            throw;
        }
        return dashboard.ToDto();
    }
}

/// <summary>Puts another picture in a picture widget. Its place, fit, description and link stay.</summary>
public sealed class ReplaceImageWidgetHandler(
    IDashboardRepository dashboards, IImageStore images, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<DashboardDto> HandleAsync(Guid organizationId, Guid dashboardId, Guid widgetId, Stream image, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);

        var definition = DashboardJson.Deserialize(dashboard.DefinitionJson);
        var current = ImageWidgets.Find(definition, widgetId);

        var bytes = await ImageUpload.ReadAsync(image, "Choose a picture to put on the dashboard.", ct);
        var kind = ImageUpload.Kind(bytes);
        var key = await images.SaveAsync(bytes, kind.Extension, ct);

        try
        {
            var changed = definition with { Widgets = definition.Widgets.Select(w => w.Id == widgetId ? w with { ImageKey = key } : w).ToList() };
            DatasetQuery.Guard(() =>
            {
                dashboard.Update(dashboard.Name, dashboard.Description, DashboardJson.Serialize(changed), clock.UtcNow);
                return true;
            });
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            images.Delete(key);
            throw;
        }

        images.Delete(current.ImageKey!);
        return dashboard.ToDto();
    }
}

/// <summary>The picture of a picture widget, for any member of the organization.</summary>
public sealed class GetImageWidgetHandler(IDashboardRepository dashboards, IImageStore images, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task<BackgroundImage> HandleAsync(Guid organizationId, Guid dashboardId, Guid widgetId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);
        var dashboard = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        return Open(dashboard, widgetId, images);
    }

    internal static BackgroundImage Open(Dashboard dashboard, Guid widgetId, IImageStore images)
    {
        var widget = ImageWidgets.Find(DashboardJson.Deserialize(dashboard.DefinitionJson), widgetId);
        var stream = images.Open(widget.ImageKey!) ?? throw new NotFoundException("Picture not found.");
        return new BackgroundImage(stream, ImageSniffer.ContentTypeOfKey(widget.ImageKey!));
    }
}

/// <summary>The picture of a picture widget, for a visitor with a valid link (and only on that link's dashboard).</summary>
public sealed class GetSharedImageWidgetHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, IImageStore images, IClock clock, SharedLinkAccess access)
{
    public async Task<BackgroundImage> HandleAsync(string? token, Guid widgetId, CancellationToken ct = default)
    {
        var (_, dashboard) = await SharedLinks.ResolveAsync(token, shares, dashboards, clock, access, ct);
        return GetImageWidgetHandler.Open(dashboard, widgetId, images);
    }
}
