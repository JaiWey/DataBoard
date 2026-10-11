using DataBoard.Application.Organizations;
using DataBoard.Domain.Organizations;
using Microsoft.EntityFrameworkCore;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class OrganizationRepository(AppDbContext db) : IOrganizationRepository
{
    public async Task AddAsync(Organization organization, OrganizationMember owner, CancellationToken ct = default)
    {
        await db.Organizations.AddAsync(organization, ct);
        await db.OrganizationMembers.AddAsync(owner, ct);
    }

    public async Task AddMemberAsync(OrganizationMember member, CancellationToken ct = default) =>
        await db.OrganizationMembers.AddAsync(member, ct);

    public Task<OrganizationMember?> GetMemberAsync(Guid organizationId, Guid userId, CancellationToken ct = default) =>
        db.OrganizationMembers
            .AsNoTracking()
            .SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct);

    public async Task<IReadOnlyList<OrganizationMember>> ListMembersAsync(Guid organizationId, CancellationToken ct = default) =>
        await db.OrganizationMembers.AsNoTracking().Where(m => m.OrganizationId == organizationId).ToListAsync(ct);

    public Task<OrganizationMember?> GetMemberForUpdateAsync(Guid organizationId, Guid userId, CancellationToken ct = default) =>
        db.OrganizationMembers.SingleOrDefaultAsync(m => m.OrganizationId == organizationId && m.UserId == userId, ct);

    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(IReadOnlyCollection<Guid> organizationIds, CancellationToken ct = default) =>
        await db.Organizations.AsNoTracking().Where(o => organizationIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Name, ct);

    public Task<bool> IsDefaultAsync(Guid organizationId, CancellationToken ct = default) =>
        db.Organizations.AsNoTracking().AnyAsync(o => o.Id == organizationId && o.IsDefault, ct);

    public Task<int> CountOwnersAsync(Guid organizationId, CancellationToken ct = default) =>
        db.OrganizationMembers.CountAsync(m => m.OrganizationId == organizationId && m.Role == OrganizationRole.Owner, ct);

    public void RemoveMember(OrganizationMember member) => db.OrganizationMembers.Remove(member);

    public async Task<IReadOnlyList<OrganizationSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        await db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new { o.Id, o.Name, m.Role, o.IsDefault })
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new OrganizationSummary(x.Id, x.Name, x.Role, x.IsDefault))
            .ToListAsync(ct);
}
