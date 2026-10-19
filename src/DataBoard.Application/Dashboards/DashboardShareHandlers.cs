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
/// <param name="OrganizationIds">
/// Empty or missing: anyone with the link can open it. Otherwise only signed-in members of these organizations can. The
/// person making the link has to belong to each of them.
/// </param>
public sealed record CreateShareRequest(int? ExpiresInDays = null, bool NeverExpires = false, IReadOnlyList<Guid>? OrganizationIds = null);

/// <param name="OrganizationIds">Who may open it: empty is anyone with the link.</param>
/// <param name="Token">
/// The secret in the link, so an editor can copy it again. Missing for a link made before it was kept (its address cannot be
/// shown) and when it can no longer be decrypted. Only editors get this list.
/// </param>
public sealed record ShareDto(
    Guid Id, DateTime CreatedUtc, DateTime? ExpiresUtc, bool Expired, IReadOnlyList<Guid>? OrganizationIds = null, string? Token = null);

/// <param name="Token">The secret in the link.</param>
public sealed record ShareCreatedDto(Guid Id, string Token, DateTime CreatedUtc, DateTime? ExpiresUtc, IReadOnlyList<Guid>? OrganizationIds = null);

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
    DashboardBackgroundDto? Background = null,
    IReadOnlyList<DashboardTab>? Tabs = null,
    int? ReloadSeconds = null,
    string? SharedBy = null);

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

/// <summary>
/// Whether the person opening a link may. A public link lets everyone in. A limited one needs somebody signed in who
/// belongs to one of its organizations: nobody signed in gets 401 (so the page can ask them to), somebody who is signed
/// in but not a member gets 403.
/// </summary>
public sealed class SharedLinkAccess(ICurrentUser currentUser, IOrganizationRepository organizations)
{
    public async Task RequireAsync(DashboardShare share, CancellationToken ct = default)
    {
        if (!share.IsRestricted) return;

        var userId = currentUser.UserId ?? throw new UnauthorizedException("Sign in to open this dashboard.");
        foreach (var organizationId in share.AllowedOrganizationIds)
            if (await organizations.GetMemberAsync(organizationId, userId, ct) is not null) return;

        throw new ForbiddenException("This dashboard is shared with members of specific organizations, and you are not in one of them.");
    }
}

internal static class SharedLinks
{
    public static NotFoundException NotValid() => new("This link is not valid or has expired.");

    /// <summary>Every way a link can be wrong (never existed, revoked, expired, dashboard gone) gives the same answer.</summary>
    public static async Task<(DashboardShare Share, Dashboard Dashboard)> ResolveAsync(
        string? token, IDashboardShareRepository shares, IDashboardRepository dashboards, IClock clock, SharedLinkAccess access, CancellationToken ct)
    {
        if (!ShareTokens.LooksValid(token)) throw NotValid();

        var share = await shares.FindByTokenHashAsync(ShareTokens.Hash(token!), ct);
        if (share is null || !share.IsActive(clock.UtcNow)) throw NotValid();

        var dashboard = await dashboards.GetAsync(share.OrganizationId, share.DashboardId, ct) ?? throw NotValid();

        // After the checks above, so a wrong or revoked token is the same 404 whoever asks; only a real link says who it is for.
        await access.RequireAsync(share, ct);
        return (share, dashboard);
    }
}

public sealed class CreateShareHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, IOrganizationRepository organizations,
    IUnitOfWork unitOfWork, IClock clock, OrganizationAccess access, DashboardVisibility visibility, ISecretProtector secrets)
{
    public async Task<ShareCreatedDto> HandleAsync(
        Guid organizationId, Guid dashboardId, CreateShareRequest? request, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        access.RequireNotGuest(); // a public link would show the sandbox to anyone
        var userId = access.RequireUserId();
        _ = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);

        var now = clock.UtcNow;
        DateTime? expires = null;
        if (request is not { NeverExpires: true })
        {
            if (request?.ExpiresInDays is not { } days || days < 1 || days > DashboardShare.MaxLifetimeDays)
                throw new ValidationException("ExpiresInDays", $"Choose how long the link lasts: 1 to {DashboardShare.MaxLifetimeDays} days, or never.");
            expires = now.AddDays(days);
        }

        // A link can only be limited to organizations its maker belongs to: they cannot hand out, or even probe, other people's.
        var allowed = (request?.OrganizationIds ?? []).Distinct().ToList();
        if (allowed.Count > DashboardShare.MaxAllowedOrganizations)
            throw new ValidationException("OrganizationIds", $"A link can be limited to at most {DashboardShare.MaxAllowedOrganizations} organizations.");
        foreach (var id in allowed)
            if (id == Guid.Empty || await organizations.GetMemberAsync(id, userId, ct) is null)
                throw new ValidationException("OrganizationIds", "A link can only be limited to organizations you belong to.");

        if (await shares.CountActiveAsync(dashboardId, now, ct) >= DashboardShare.MaxPerDashboard)
            throw new ValidationException("Shares", $"A dashboard can have at most {DashboardShare.MaxPerDashboard} active links. Revoke one first.");

        var token = ShareTokens.NewToken();
        var share = DatasetQuery.Guard(() =>
            DashboardShare.Create(organizationId, dashboardId, ShareTokens.Hash(token), userId, now, expires, allowed, secrets.Protect(token)));

        await shares.AddAsync(share, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return new ShareCreatedDto(share.Id, token, share.CreatedUtc, share.ExpiresUtc, share.AllowedOrganizationIds);
    }
}

public sealed class ListSharesHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, IClock clock, OrganizationAccess access, DashboardVisibility visibility, ISecretProtector secrets)
{
    public async Task<IReadOnlyList<ShareDto>> HandleAsync(Guid organizationId, Guid dashboardId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        _ = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);

        var now = clock.UtcNow;
        return (await shares.ListAsync(organizationId, dashboardId, ct))
            .Select(s => new ShareDto(s.Id, s.CreatedUtc, s.ExpiresUtc, !s.IsActive(now), s.AllowedOrganizationIds, Reveal(s)))
            .ToList();
    }

    /// <summary>The secret of a link, or null if none was kept or it cannot be read (the encryption keys changed).</summary>
    private string? Reveal(DashboardShare share)
    {
        if (share.EncryptedToken is not { Length: > 0 } encrypted) return null;
        try
        {
            var token = secrets.Unprotect(encrypted);
            return ShareTokens.LooksValid(token) ? token : null;
        }
        catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or FormatException)
        {
            return null;
        }
    }
}

public sealed class RevokeShareHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, IUnitOfWork unitOfWork, OrganizationAccess access, DashboardVisibility visibility)
{
    public async Task HandleAsync(Guid organizationId, Guid dashboardId, Guid shareId, CancellationToken ct = default)
    {
        var member = await access.RequireRoleAsync(organizationId, OrganizationRole.Editor, ct);
        _ = await visibility.GetAsync(dashboards, organizationId, dashboardId, member, ct);
        var share = await shares.GetAsync(organizationId, dashboardId, shareId, ct) ?? throw new NotFoundException("Link not found.");
        shares.Remove(share);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>What a visitor with a link sees first: the dashboard and just enough about its charts to draw them.</summary>
public sealed class GetSharedDashboardHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, IChartRepository charts, IClock clock, SharedLinkAccess access)
{
    public async Task<SharedDashboardDto> HandleAsync(string? token, CancellationToken ct = default)
    {
        var (share, dashboard) = await SharedLinks.ResolveAsync(token, shares, dashboards, clock, access, ct);
        return await SharedPages.BuildAsync(charts, share.OrganizationId, dashboard, share.ExpiresUtc, sharedBy: null, ct);
    }
}

/// <summary>
/// The read-only page of a dashboard for somebody who is not one of its editors: through a link, or because another
/// organization shares it. Names no query, dataset or column.
/// </summary>
internal static class SharedPages
{
    public static async Task<SharedDashboardDto> BuildAsync(
        IChartRepository charts, Guid organizationId, Dashboard dashboard, DateTime? expiresUtc, string? sharedBy, CancellationToken ct)
    {
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

            var chart = await charts.GetAsync(organizationId, widget.ChartId, ct);
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
            expiresUtc,
            dashboard.ToBackgroundDto(),
            definition.Tabs,
            definition.ReloadSeconds,
            sharedBy);
    }
}

public sealed class RunSharedDashboardHandler(
    IDashboardShareRepository shares, IDashboardRepository dashboards, RunDashboardHandler runner, IClock clock, SharedLinkAccess access)
{
    public async Task<DashboardRunResult> HandleAsync(string? token, RunDashboardRequest? request, CancellationToken ct = default)
    {
        var (share, dashboard) = await SharedLinks.ResolveAsync(token, shares, dashboards, clock, access, ct);
        return await runner.HandleSharedAsync(share.OrganizationId, DashboardJson.Deserialize(dashboard.DefinitionJson), request, ct);
    }
}
