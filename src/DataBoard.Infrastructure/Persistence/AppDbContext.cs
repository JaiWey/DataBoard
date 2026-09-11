using DataBoard.Application.Abstractions;
using DataBoard.Application.Common;
using DataBoard.Domain.Charts;
using DataBoard.Domain.Dashboards;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;
using DataBoard.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace DataBoard.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options), IUnitOfWork
{
    public DbSet<DataSource> DataSources => Set<DataSource>();
    public DbSet<Dataset> Datasets => Set<Dataset>();
    public DbSet<Dashboard> Dashboards => Set<Dashboard>();
    public DbSet<DashboardShare> DashboardShares => Set<DashboardShare>();
    public DbSet<Chart> Charts => Set<Chart>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMember> OrganizationMembers => Set<OrganizationMember>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <summary>A duplicate name that slips past the pre-check (two requests at once) becomes a 409, not a 500.</summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("That name is already in use.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Consistent snake_case table names instead of the AspNet* defaults.
        modelBuilder.Entity<AppUser>().ToTable("users");
        modelBuilder.Entity<IdentityRole<Guid>>().ToTable("roles");
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles");
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
