using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Identity;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Organizations;

public sealed record CreateOrganizationRequest(string Name);
public sealed record AddMemberRequest(string Email, OrganizationRole Role);
public sealed record OrganizationDto(Guid Id, string Name, OrganizationRole Role, bool IsDefault);

public sealed class CreateOrganizationHandler(
    IOrganizationRepository organizations,
    IUnitOfWork unitOfWork,
    OrganizationAccess access,
    IClock clock)
{
    public async Task<OrganizationDto> HandleAsync(CreateOrganizationRequest request, CancellationToken ct = default)
    {
        var userId = access.RequireUserId();
        access.RequireNotGuest();
        var now = clock.UtcNow;

        Organization organization;
        try
        {
            organization = Organization.Create(request.Name, now);
        }
        catch (ArgumentException ex)
        {
            throw new ValidationException(nameof(request.Name), ex.Message);
        }

        var owner = OrganizationMember.Create(organization.Id, userId, OrganizationRole.Owner, now);
        await organizations.AddAsync(organization, owner, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return new OrganizationDto(organization.Id, organization.Name, OrganizationRole.Owner, false);
    }
}

public sealed class ListMyOrganizationsHandler(IOrganizationRepository organizations, OrganizationAccess access)
{
    public async Task<IReadOnlyList<OrganizationDto>> HandleAsync(CancellationToken ct = default)
    {
        var userId = access.RequireUserId();
        var items = await organizations.ListForUserAsync(userId, ct);
        return items.Select(o => new OrganizationDto(o.Id, o.Name, o.Role, o.IsDefault)).ToList();
    }
}

/// <param name="UserId">Also what to send to change or remove them.</param>
public sealed record OrganizationMemberDto(Guid UserId, string Email, string DisplayName, OrganizationRole Role, DateTime JoinedUtc);
public sealed record ChangeMemberRoleRequest(OrganizationRole Role);

/// <summary>Everyone in the organization, Owners first. Any member may look: they work together and see each other's names.</summary>
public sealed class ListMembersHandler(IOrganizationRepository organizations, IIdentityService identity, OrganizationAccess access)
{
    public async Task<IReadOnlyList<OrganizationMemberDto>> HandleAsync(Guid organizationId, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Viewer, ct);

        var members = await organizations.ListMembersAsync(organizationId, ct);
        var people = (await identity.GetUsersAsync(members.Select(m => m.UserId).ToList(), ct)).ToDictionary(u => u.Id);

        return members
            .Where(m => people.ContainsKey(m.UserId))
            .Select(m => new OrganizationMemberDto(m.UserId, people[m.UserId].Email, people[m.UserId].DisplayName, m.Role, m.JoinedUtc))
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(m => m.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

public static class MemberRules
{
    public const string LastOwner = "An organization needs at least one Owner. Make someone else an Owner first.";
    public const string DefaultOrganization = "Everyone belongs to the default organization, so nobody can be removed from it or leave it.";
}

/// <summary>Owners only. Taking the role away from the last Owner is refused, so an organization is never left without one.</summary>
public sealed class ChangeMemberRoleHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task HandleAsync(Guid organizationId, Guid userId, ChangeMemberRoleRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Owner, ct);

        if (!Enum.IsDefined(request.Role))
            throw new ValidationException(nameof(request.Role), "Unknown role.");

        var member = await organizations.GetMemberForUpdateAsync(organizationId, userId, ct)
            ?? throw new NotFoundException("That user is not a member.");
        if (member.Role == request.Role) return;

        if (member.Role == OrganizationRole.Owner && await organizations.CountOwnersAsync(organizationId, ct) <= 1)
            throw new ConflictException(MemberRules.LastOwner);

        member.ChangeRole(request.Role);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

/// <summary>
/// An Owner removes anyone; anyone may remove themselves (leave). What a person made stays with the organization. Their
/// access ends at once, because every request checks the membership; so does their personal access token's.
/// </summary>
public sealed class RemoveMemberHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork, OrganizationAccess access)
{
    public async Task HandleAsync(Guid organizationId, Guid userId, CancellationToken ct = default)
    {
        var leaving = access.RequireUserId() == userId;
        await access.RequireRoleAsync(organizationId, leaving ? OrganizationRole.Viewer : OrganizationRole.Owner, ct);

        if (await organizations.IsDefaultAsync(organizationId, ct))
            throw new ConflictException(MemberRules.DefaultOrganization);

        var member = await organizations.GetMemberForUpdateAsync(organizationId, userId, ct)
            ?? throw new NotFoundException("That user is not a member.");

        if (member.Role == OrganizationRole.Owner && await organizations.CountOwnersAsync(organizationId, ct) <= 1)
            throw new ConflictException(MemberRules.LastOwner);

        organizations.RemoveMember(member);
        await unitOfWork.SaveChangesAsync(ct);
    }
}

public sealed class AddMemberHandler(
    IOrganizationRepository organizations,
    IIdentityService identity,
    IUnitOfWork unitOfWork,
    OrganizationAccess access,
    IClock clock)
{
    public async Task HandleAsync(Guid organizationId, AddMemberRequest request, CancellationToken ct = default)
    {
        await access.RequireRoleAsync(organizationId, OrganizationRole.Owner, ct);

        if (!Enum.IsDefined(request.Role))
            throw new ValidationException(nameof(request.Role), "Unknown role.");

        var userId = await identity.FindUserIdByEmailAsync(request.Email, ct)
            ?? throw new NotFoundException("No user with that email.");

        if (await organizations.GetMemberAsync(organizationId, userId, ct) is not null)
            throw new ConflictException("That user is already a member.");

        await organizations.AddMemberAsync(
            OrganizationMember.Create(organizationId, userId, request.Role, clock.UtcNow), ct);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
