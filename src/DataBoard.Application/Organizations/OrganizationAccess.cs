using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Organizations;

/// <summary>The single place that answers "may the current user do X in organization Y?".</summary>
public sealed class OrganizationAccess(IOrganizationRepository organizations, ICurrentUser currentUser)
{
    public Guid RequireUserId() =>
        currentUser.UserId ?? throw new UnauthorizedException();

    /// <summary>Guests may try the app inside their own sandbox, but not do what reaches outside it.</summary>
    public void RequireNotGuest()
    {
        if (currentUser.IsGuest)
            throw new ForbiddenException("Guests cannot do this. Create an account to unlock it.");
    }

    /// <summary>
    /// Non-members get NotFound (not Forbidden) so organization ids cannot be probed.
    /// Members without enough privilege get Forbidden.
    /// </summary>
    public async Task<OrganizationMember> RequireRoleAsync(
        Guid organizationId, OrganizationRole minimum, CancellationToken ct = default)
    {
        var userId = RequireUserId();
        var member = await organizations.GetMemberAsync(organizationId, userId, ct)
            ?? throw new NotFoundException("Organization not found.");

        if (!member.HasAtLeast(minimum))
            throw new ForbiddenException($"This action requires the {minimum} role.");

        return member;
    }
}
