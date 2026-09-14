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

        builder.Property(d => d.RefreshIntervalMinutes).HasColumnName("refresh_interval_minutes");
        builder.Property(d => d.NextRefreshUtc).HasColumnName("next_refresh_utc");
        builder.Property(d => d.LastRefreshUtc).HasColumnName("last_refresh_utc");
        builder.Property(d => d.LastRefreshFailures).HasColumnName("last_refresh_failures").HasDefaultValue(0).HasSentinel(0);
        builder.Property(d => d.LastRefreshMessage).HasColumnName("last_refresh_message").HasMaxLength(Dashboard.MaxRefreshMessageLength);

        builder.HasIndex(d => new { d.OrganizationId, d.Name }).IsUnique();
        // The scheduler asks "what is due?" every few seconds; only dashboards with a schedule are in this index.
        builder.HasIndex(d => d.NextRefreshUtc).HasFilter("refresh_interval_minutes IS NOT NULL");

        builder.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(d => d.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
