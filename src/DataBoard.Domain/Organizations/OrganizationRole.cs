namespace DataBoard.Domain.Organizations;

/// <summary>Ordered by privilege: a higher value includes everything a lower value can do.</summary>
public enum OrganizationRole
{
    Viewer = 1,
    Editor = 2,
    Owner = 3,
}
