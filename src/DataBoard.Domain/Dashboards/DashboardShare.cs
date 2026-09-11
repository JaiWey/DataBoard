using DataBoard.Domain.Common;

namespace DataBoard.Domain.Dashboards;

/// <summary>
/// A public, read-only link to one dashboard. Anyone who has the link can see the dashboard's charts, so the link is
/// the secret: only a hash of it is stored (a copy of the database does not hand out working links), and revoking a
/// link deletes it.
/// </summary>
public sealed class DashboardShare : BaseEntity
{
    public const int TokenHashLength = 64; // SHA-256 as hex
    public const int MaxPerDashboard = 10;
    public const int MaxLifetimeDays = 365;

    private DashboardShare() { }

    public Guid OrganizationId { get; private set; }
    public Guid DashboardId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    /// <summary>Null means the link never expires (until it is revoked).</summary>
    public DateTime? ExpiresUtc { get; private set; }

    public static DashboardShare Create(
        Guid organizationId, Guid dashboardId, string tokenHash, Guid createdByUserId, DateTime nowUtc, DateTime? expiresUtc)
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

        return new DashboardShare
        {
            OrganizationId = organizationId,
            DashboardId = dashboardId,
            TokenHash = tokenHash,
            CreatedByUserId = createdByUserId,
            CreatedUtc = nowUtc,
            ExpiresUtc = expiresUtc,
        };
    }

    public bool IsActive(DateTime nowUtc) => ExpiresUtc is null || ExpiresUtc > nowUtc;
}
