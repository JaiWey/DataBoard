using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Application.Identity;
using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Organizations;

public sealed record CreateOrganizationRequest(string Name);
public sealed record AddMemberRequest(string Email, OrganizationRole Role);
public sealed record OrganizationDto(Guid Id, string Name, OrganizationRole Role);

public sealed class CreateOrganizationHandler(
    IOrganizationRepository organizations,
    IUnitOfWork unitOfWork,
    OrganizationAccess access,
    IClock clock)
{
    public async Task<OrganizationDto> HandleAsync(CreateOrganizationRequest request, CancellationToken ct = default)
    {
        var userId = access.RequireUserId();
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

        return new OrganizationDto(organization.Id, organization.Name, OrganizationRole.Owner);
    }
}

public sealed class ListMyOrganizationsHandler(IOrganizationRepository organizations, OrganizationAccess access)
{
    public async Task<IReadOnlyList<OrganizationDto>> HandleAsync(CancellationToken ct = default)
    {
        var userId = access.RequireUserId();
        var items = await organizations.ListForUserAsync(userId, ct);
        return items.Select(o => new OrganizationDto(o.Id, o.Name, o.Role)).ToList();
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
