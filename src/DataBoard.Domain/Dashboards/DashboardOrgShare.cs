using DataBoard.Domain.Common;

namespace DataBoard.Domain.Dashboards;

/// <summary>
/// A dashboard shown, read-only, to every member of another organization. The dashboard stays where it is, owned and
/// edited by its own organization; the other one sees it live in its dashboards list and can open and filter it, but never
/// change it and never see its queries. Taking the share away takes it out of their list at once.
/// </summary>
public sealed class DashboardOrgShare : BaseEntity
{
    public const int MaxPerDashboard = 20;

    private DashboardOrgShare() { }

    /// <summary>The organization the dashboard belongs to.</summary>
    public Guid OrganizationId { get; private set; }
    public Guid DashboardId { get; private set; }

    /// <summary>The organization that gets to see it.</summary>
    public Guid TargetOrganizationId { get; private set; }

    public Guid SharedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    public static DashboardOrgShare Create(
        Guid organizationId, Guid dashboardId, Guid targetOrganizationId, Guid sharedByUserId, DateTime nowUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (dashboardId == Guid.Empty)
            throw new ArgumentException("Dashboard is required.", nameof(dashboardId));
        if (targetOrganizationId == Guid.Empty)
            throw new ArgumentException("The organization to share with is required.", nameof(targetOrganizationId));
        if (targetOrganizationId == organizationId)
            throw new ArgumentException("A dashboard is already in its own organization.", nameof(targetOrganizationId));
        if (sharedByUserId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(sharedByUserId));

        return new DashboardOrgShare
        {
            OrganizationId = organizationId,
            DashboardId = dashboardId,
            TargetOrganizationId = targetOrganizationId,
            SharedByUserId = sharedByUserId,
            CreatedUtc = nowUtc,
        };
    }
}
