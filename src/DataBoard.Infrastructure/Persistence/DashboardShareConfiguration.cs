using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Organizations;
using DataBoard.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DashboardShareConfiguration : IEntityTypeConfiguration<DashboardShare>
{
    public void Configure(EntityTypeBuilder<DashboardShare> builder)
    {
        builder.ToTable("dashboard_shares");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TokenHash).IsRequired().HasMaxLength(DashboardShare.TokenHashLength).IsFixedLength();

        // A link is found by its token, on every visit.
        builder.HasIndex(s => s.TokenHash).IsUnique();
        builder.HasIndex(s => s.DashboardId);

        builder.HasOne<Dashboard>().WithMany().HasForeignKey(s => s.DashboardId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Organization>().WithMany().HasForeignKey(s => s.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(s => s.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
