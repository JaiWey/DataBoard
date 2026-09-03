namespace DataBoard.Domain.Organizations;

/// <summary>Links a user (by id; the domain does not know the identity system) to an organization with a role.</summary>
public sealed class OrganizationMember
{
    private OrganizationMember() { }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public OrganizationRole Role { get; private set; }
    public DateTime JoinedUtc { get; private set; }

    public static OrganizationMember Create(Guid organizationId, Guid userId, OrganizationRole role, DateTime joinedUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (userId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(userId));
        if (!Enum.IsDefined(role))
            throw new ArgumentException("Unknown role.", nameof(role));

        return new OrganizationMember
        {
            OrganizationId = organizationId,
            UserId = userId,
            Role = role,
            JoinedUtc = joinedUtc,
        };
    }

    public bool HasAtLeast(OrganizationRole required) => Role >= required;
}
