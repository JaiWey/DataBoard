using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Organizations;
using DataBoard.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DashboardOrgShareConfiguration : IEntityTypeConfiguration<DashboardOrgShare>
{
    public void Configure(EntityTypeBuilder<DashboardOrgShare> builder)
    {
        builder.ToTable("dashboard_org_shares");
        builder.HasKey(s => s.Id);

        // One share per dashboard and organization; a dashboard's shares and an organization's incoming ones are both listed.
        builder.HasIndex(s => new { s.DashboardId, s.TargetOrganizationId }).IsUnique();
        builder.HasIndex(s => s.TargetOrganizationId);

        builder.HasOne<Dashboard>().WithMany().HasForeignKey(s => s.DashboardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(s => s.TargetOrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.SharedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
