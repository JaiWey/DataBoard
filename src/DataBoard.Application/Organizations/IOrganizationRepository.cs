using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Organizations;

public sealed record OrganizationSummary(Guid Id, string Name, OrganizationRole Role);

public interface IOrganizationRepository
{
    Task AddAsync(Organization organization, OrganizationMember owner, CancellationToken ct = default);
    Task AddMemberAsync(OrganizationMember member, CancellationToken ct = default);
    Task<OrganizationMember?> GetMemberAsync(Guid organizationId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default);
}
