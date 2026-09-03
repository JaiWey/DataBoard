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

    public async Task<IReadOnlyList<OrganizationSummary>> ListForUserAsync(Guid userId, CancellationToken ct = default) =>
        await db.OrganizationMembers
            .AsNoTracking()
            .Where(m => m.UserId == userId)
            .Join(db.Organizations, m => m.OrganizationId, o => o.Id, (m, o) => new { o.Id, o.Name, m.Role })
            .OrderBy(x => x.Name)
            .Select(x => new OrganizationSummary(x.Id, x.Name, x.Role))
            .ToListAsync(ct);
}
