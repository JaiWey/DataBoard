using DataBoard.Domain.Dashboards;
using DataBoard.Domain.Organizations;
using DataBoard.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DashboardConfiguration : IEntityTypeConfiguration<Dashboard>
{
    public void Configure(EntityTypeBuilder<Dashboard> builder)
    {
        builder.ToTable("dashboards");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(Dashboard.MaxNameLength);
        builder.Property(d => d.Description).HasMaxLength(Dashboard.MaxDescriptionLength);
        builder.Property(d => d.DefinitionJson).IsRequired().HasColumnName("definition").HasColumnType("jsonb");

        builder.HasIndex(d => new { d.OrganizationId, d.Name }).IsUnique();

        builder.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(d => d.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
