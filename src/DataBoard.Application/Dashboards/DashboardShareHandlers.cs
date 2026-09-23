using System.Security.Cryptography;
using System.Text;
using DataBoard.Application.Abstractions;
using DataBoard.Application.Charts;
using DataBoard.Application.Common;
using DataBoard.Application.Datasets;
using DataBoard.Application.Organizations;
using DataBoard.Domain.Charts;
using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Dashboards;

/// <summary>
/// How long a new link lives: a number of days (1 to 365), or <paramref name="NeverExpires"/>. One of the two has to
/// be said, so a link is never public forever by leaving a field out.
/// </summary>
public sealed record CreateShareRequest(int? ExpiresInDays = null, bool NeverExpires = false);

public sealed record ShareDto(Guid Id, DateTime CreatedUtc, DateTime? ExpiresUtc, bool Expired);

/// <param name="Token">The secret in the link. It is shown once, here: only its hash is kept.</param>
public sealed record ShareCreatedDto(Guid Id, string Token, DateTime CreatedUtc, DateTime? ExpiresUtc);

/// <summary>What the public page needs to draw one chart. No query, dataset or column names.</summary>
public sealed record SharedChartDto(Guid Id, string Name, ChartType Type, ChartStyle Style, int DimensionCount);

public sealed record SharedFilterDto(Guid Id, string Label, FilterOperator Operator);

public sealed record SharedDashboardDto(
    string Name,
    string? Description,
    IReadOnlyList<DashboardWidget> Widgets,
    IReadOnlyList<SharedFilterDto> Filters,
    IReadOnlyList<SharedChartDto> Charts,
    DateTime? ExpiresUtc,
    DashboardBackgroundDto? Background = null);

internal static class ShareTokens
{
    private const int TokenBytes = 32; // 256 bits: cannot be guessed
    private const int TokenLength = 43; // the same bytes as unpadded base64url

    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    /// <summary>A cheap check before touching the database: anything that is not shaped like one of our tokens cannot be one.</summary>
    public static bool LooksValid(string? token) =>
        token is { Length: TokenLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

internal static class SharedLinks
{
    public static NotFoundException NotValid() => new("This link is not valid or has expired.");

    /// <summary>Every way a link can be wrong (never existed, revoked, expired, dashboard gone) gives the same answer.</summary>
    public static async Task<(DashboardShare Share, Dashboard Dashboard)> ResolveAsync(
        string? token, IDashboardShareRepository shares, IDashboardRepository dashboards, IClock clock, CancellationToken ct)
    {
        if (!ShareTokens.LooksValid(token)) throw NotValid();

        var share = await shares.FindByTokenHashAsync(ShareTokens.Hash(token!), ct);
        if (share is null || !share.IsActive(clock.UtcNow)) throw NotValid();

        var dashboard = await dashboards.GetAsync(share.OrganizationId, share.DashboardId, ct) ?? throw NotValid();
        return (share, dashboard);
    }
}

public sealed class CreateShareHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access)
{
    public async Task<ShareCreatedDto> HandleAsync(
        Guid organizationId, Guid dashboardId, CreateShareRequest? request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var userId = access.RequireUserId();
        _ = await dashboards.GetAsync(organizationId, dashboardId, ct) ?? throw new NotFoundException("Dashboard not found.");

        var now = clock.UtcNow;
        DateTime? expires = null;
        if (request is not { NeverExpires: true })
        {
            if (request?.ExpiresInDays is not { } days || days < 1 || days > DashboardShare.MaxLifetimeDays)
                throw new ValidationException("ExpiresInDays", $"Choose how long the link lasts: 1 to {DashboardShare.MaxLifetimeDays} days, or never.");
            expires = now.AddDays(days);
        }

        if (await shares.CountActiveAsync(dashboardId, now, ct) >= DashboardShare.MaxPerDashboard)
            throw new ValidationException("Shares", $"A dashboard can have at most {DashboardShare.MaxPerDashboard} active links. Revoke one first.");

        var token = ShareTokens.NewToken();
        var share = DatasetQuery.Guard(() => DashboardShare.Create(organizationId, dashboardId, ShareTokens.Hash(token), userId, now, expires));

        await shares.AddAsync(share, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return new ShareCreatedDto(share.Id, token, share.CreatedUtc, share.ExpiresUtc);
    }
}

public sealed class ListSharesHandler(IDashboardShareRepository shares, IDashboardRepository dashboards, IClock clock, OrganizationAccess access)
{
    public async Task<IReadOnlyList<ShareDto>> HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        _ = await dashboards.GetAsync(organizationId, dashboardId, ct) ?? throw new NotFoundException("Dashboard not found.");

        var now = clock.UtcNow;
        return (await shares.ListAsync(organizationId, dashboardId, ct))
            .Select(s => new ShareDto(s.Id, s.CreatedUtc, s.ExpiresUtc, !s.IsActive(now)))
            .ToList();
    }
}

public sealed class RevokeShareHandler(IDashboardShareRepository shares, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task HandleAsync(Guid organizationId, Guid dashboardId, Guid shareId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        var share = await shares.GetAsync(organizationId, dashboardId, shareId, ct) ?? throw new NotFoundException("Link not found.");
        shares.Remove(share);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>What a visitor with a link sees first: the dashboard and just enough about its charts to draw them.</summary>
public sealed class GetSharedDashboardHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, IChartRepository charts, IClock clock)
{
    public async Task<SharedDashboardDto> HandleAsync(string? token, CancellationToken ct = default)
    {
        var (share, dashboard) = await SharedLinks.ResolveAsync(token, shares, dashboards, clock, ct);
        var definition = DashboardJson.Deserialize(dashboard.DefinitionJson);

        var shown = new List<SharedChartDto>();
        var widgets = new List<DashboardWidget>();
        foreach (var widget in definition.Widgets)
        {
            if (widget.Kind == WidgetKind.Image)
            {
                // The public page needs to know a picture is there and whether it changed, not where it is kept.
                if (widget.ImageKey is { } pictureKey)
                    widgets.Add(widget with { ImageKey = Path.GetFileNameWithoutExtension(pictureKey) });
                continue;
            }

            if (widget.Kind == WidgetKind.Text)
            {
                widgets.Add(widget); // words on a dashboard are for everyone who can see it
                continue;
            }

            var chart = await charts.GetAsync(share.OrganizationId, widget.ChartId, ct);
            if (chart is null) continue; // a deleted chart simply is not there for the public

            var chartDefinition = ChartDefinition.FromJson(chart.DefinitionJson);
            shown.Add(new SharedChartDto(chart.Id, chart.Name, chart.Type, chartDefinition.Style ?? new ChartStyle(), chartDefinition.Query.Dimensions?.Count ?? 0));
            widgets.Add(widget);
        }

        return new SharedDashboardDto(
            dashboard.Name,
            dashboard.Description,
            widgets,
            definition.Filters.Select(f => new SharedFilterDto(f.Id, f.Label, f.Operator)).ToList(),
            shown,
            share.ExpiresUtc,
            dashboard.ToBackgroundDto());
    }
}

public sealed class RunSharedDashboardHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, RunDashboardHandler runner, IClock clock)
{
    public async Task<DashboardRunResult> HandleAsync(string? token, RunDashboardRequest? request, CancellationToken ct = default)
    {
        var (share, dashboard) = await SharedLinks.ResolveAsync(token, shares, dashboards, clock, ct);
        return await runner.HandleSharedAsync(share.OrganizationId, DashboardJson.Deserialize(dashboard.DefinitionJson), request, ct);
    }
}
