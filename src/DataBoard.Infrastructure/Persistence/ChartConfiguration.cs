using DataBoard.Domain.Charts;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Folders;
using DataBoard.Domain.Organizations;
using DataBoard.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class ChartConfiguration : IEntityTypeConfiguration<Chart>
{
    public void Configure(EntityTypeBuilder<Chart> builder)
    {
        builder.ToTable("charts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired().HasMaxLength(Chart.MaxNameLength);
        builder.Property(c => c.Description).HasMaxLength(Chart.MaxDescriptionLength);
        builder.Property(c => c.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.DefinitionJson).IsRequired().HasColumnName("definition").HasColumnType("jsonb");

        builder.HasIndex(c => new { c.OrganizationId, c.Name }).IsUnique();
        builder.HasIndex(c => c.DatasetId);
        builder.HasIndex(c => c.FolderId);

        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Dataset>().WithMany().HasForeignKey(c => c.DatasetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Folder>().WithMany().HasForeignKey(c => c.FolderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(c => c.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
