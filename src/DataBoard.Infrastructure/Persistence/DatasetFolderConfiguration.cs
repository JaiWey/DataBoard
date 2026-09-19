using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;
using DataBoard.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DatasetFolderConfiguration : IEntityTypeConfiguration<DatasetFolder>
{
    public void Configure(EntityTypeBuilder<DatasetFolder> builder)
    {
        builder.ToTable("dataset_folders");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Name).IsRequired().HasMaxLength(DatasetFolder.MaxNameLength);

        // Names are unique among siblings (the handlers compare ignoring case; the index catches two requests at once).
        // A null parent never equals another null in a unique index, so the top level gets its own index.
        builder.HasIndex(f => new { f.OrganizationId, f.ParentId, f.Name }).IsUnique()
            .HasFilter("\"ParentId\" IS NOT NULL").HasDatabaseName("ix_dataset_folders_sibling_name");
        builder.HasIndex(f => new { f.OrganizationId, f.Name }).IsUnique()
            .HasFilter("\"ParentId\" IS NULL").HasDatabaseName("ix_dataset_folders_top_level_name");

        builder.HasOne<Organization>().WithMany().HasForeignKey(f => f.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DatasetFolder>().WithMany().HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(f => f.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
