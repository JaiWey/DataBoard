using DataBoard.Domain.Common;

namespace DataBoard.Domain.Dashboards;

/// <summary>
/// A read-only link to one dashboard. By default it is public: anyone who has the link can see the dashboard's charts, so
/// the link is the secret. Visitors are found by a hash of it. The secret is also kept encrypted (<see cref="EncryptedToken"/>),
/// so an editor can copy the link again later; a copy of the database alone does not hand out working links. Revoking a link
/// deletes it. A link can instead be limited to signed-in members of chosen organizations
/// (<see cref="AllowedOrganizationIds"/>); the link is still needed, but no longer enough.
/// </summary>
public sealed class DashboardShare : BaseEntity
{
    public const int TokenHashLength = 64; // SHA-256 as hex
    public const int MaxPerDashboard = 10;
    public const int MaxLifetimeDays = 365;
    public const int MaxAllowedOrganizations = 20;
    public const int MaxEncryptedTokenLength = 1_000;

    private DashboardShare() { }

    public Guid OrganizationId { get; private set; }
    public Guid DashboardId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>
    /// The secret of the link, encrypted, for showing the link to its editors again. Null for a link made before this was kept,
    /// or one whose secret cannot be read any more; such a link works as before but its address can not be shown.
    /// </summary>
    public string? EncryptedToken { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    /// <summary>Null means the link never expires (until it is revoked).</summary>
    public DateTime? ExpiresUtc { get; private set; }

    /// <summary>
    /// Empty: anyone with the link. Otherwise only people who are signed in and belong to at least one of these
    /// organizations (whatever their role there). Membership is checked on every visit, so leaving an organization ends access.
    /// </summary>
    public Guid[] AllowedOrganizationIds { get; private set; } = [];

    public bool IsRestricted => AllowedOrganizationIds.Length > 0;

    public static DashboardShare Create(
        Guid organizationId, Guid dashboardId, string tokenHash, Guid createdByUserId, DateTime nowUtc, DateTime? expiresUtc,
        IEnumerable<Guid>? allowedOrganizationIds = null, string? encryptedToken = null)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (dashboardId == Guid.Empty)
            throw new ArgumentException("Dashboard is required.", nameof(dashboardId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(createdByUserId));
        if (tokenHash is null || tokenHash.Length != TokenHashLength)
            throw new ArgumentException("A token hash is required.", nameof(tokenHash));
        if (expiresUtc is { } expires && expires <= nowUtc)
            throw new ArgumentException("A link has to expire in the future.", nameof(expiresUtc));

        if (encryptedToken is { Length: > MaxEncryptedTokenLength })
            throw new ArgumentException("The encrypted token is too long.", nameof(encryptedToken));

        var allowed = (allowedOrganizationIds ?? []).Distinct().ToArray();
        if (allowed.Any(id => id == Guid.Empty))
            throw new ArgumentException("An organization is required.", nameof(allowedOrganizationIds));
        if (allowed.Length > MaxAllowedOrganizations)
            throw new ArgumentException($"A link can be limited to at most {MaxAllowedOrganizations} organizations.", nameof(allowedOrganizationIds));

        return new DashboardShare
        {
            AllowedOrganizationIds = allowed,
            OrganizationId = organizationId,
            DashboardId = dashboardId,
            TokenHash = tokenHash,
            EncryptedToken = encryptedToken,
            CreatedByUserId = createdByUserId,
            CreatedUtc = nowUtc,
            ExpiresUtc = expiresUtc,
        };
    }

    public bool IsActive(DateTime nowUtc) => ExpiresUtc is null || ExpiresUtc > nowUtc;
}
