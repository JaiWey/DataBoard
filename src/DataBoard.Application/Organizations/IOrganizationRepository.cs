using DataBoard.Domain.Organizations;

namespace DataBoard.Application.Organizations;

public sealed record OrganizationSummary(Guid Id, string Name, OrganizationRole Role);

public interface IOrganizationRepository
{
    Task AddAsync(Organization organization, OrganizationMember owner, CancellationToken ct = default);
    Task AddMemberAsync(OrganizationMember member, CancellationToken ct = default);
    Task<OrganizationMember?> GetMemberAsync(Guid organizationId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<OrganizationMember>> ListMembersAsync(Guid organizationId, CancellationToken ct = default);
    /// <summary>The member as something that can be changed or removed (the other getter is read-only).</summary>
    Task<OrganizationMember?> GetMemberForUpdateAsync(Guid organizationId, Guid userId, CancellationToken ct = default);
    /// <summary>The names of these organizations. Callers decide who may see them.</summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default);
    Task<int> CountOwnersAsync(Guid organizationId, CancellationToken ct = default);
    void RemoveMember(OrganizationMember member);
    Task<IReadOnlyList<OrganizationSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default);
}
