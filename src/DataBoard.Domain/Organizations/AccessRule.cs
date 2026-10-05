namespace DataBoard.Domain.Organizations;

/// <summary>
/// Who may see a dashboard or a dashboard folder, beyond the organization's own rules: people with at least a minimum
/// role, and/or particular people. A person gets in if they meet the role or are listed, and Owners of the organization
/// always get in, so a rule can never lock the organization out of its own things. A rule with neither is open: everyone
/// in the organization sees it.
/// </summary>
public sealed record AccessRule(OrganizationRole? MinRole, IReadOnlyList<Guid> MemberIds)
{
    public const int MaxMembers = 100;

    public static readonly AccessRule Open = new(null, []);

    public bool IsOpen => MinRole is null && MemberIds.Count == 0;

    public bool Allows(OrganizationRole role, Guid userId) =>
        IsOpen || role == OrganizationRole.Owner || (MinRole is { } minimum && role >= minimum) || MemberIds.Contains(userId);

    /// <summary>The rule with each person once, checked to be a rule at all. Throws <see cref="ArgumentException"/> when it is not.</summary>
    public static AccessRule Create(OrganizationRole? minRole, IEnumerable<Guid>? memberIds)
    {
        if (minRole is { } role && !Enum.IsDefined(role))
            throw new ArgumentException("Unknown role.", nameof(minRole));

        var members = (memberIds ?? []).Distinct().ToArray();
        if (members.Any(id => id == Guid.Empty))
            throw new ArgumentException("A person is required.", nameof(memberIds));
        if (members.Length > MaxMembers)
            throw new ArgumentException($"At most {MaxMembers} people can be listed.", nameof(memberIds));

        return new AccessRule(minRole, members);
    }
}
