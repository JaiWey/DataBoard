using DataBoard.Domain.Folders;
using DataBoard.Domain.Organizations;
using DataBoard.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class FolderConfiguration : IEntityTypeConfiguration<Folder>
{
    public void Configure(EntityTypeBuilder<Folder> builder)
    {
        builder.ToTable("folders");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Name).IsRequired().HasMaxLength(Folder.MaxNameLength);
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(20).HasDefaultValue(FolderKind.Dataset).HasSentinel(FolderKind.Dataset);

        // Names are unique among siblings of the same kind (the handlers compare ignoring case; the index catches two
        // requests at once). A null parent never equals another null in a unique index, so the top level gets its own index.
        builder.HasIndex(f => new { f.OrganizationId, f.Kind, f.ParentId, f.Name }).IsUnique()
            .HasFilter("\"ParentId\" IS NOT NULL").HasDatabaseName("ix_folders_sibling_name");
        builder.HasIndex(f => new { f.OrganizationId, f.Kind, f.Name }).IsUnique()
            .HasFilter("\"ParentId\" IS NULL").HasDatabaseName("ix_folders_top_level_name");

        builder.HasOne<Organization>().WithMany().HasForeignKey(f => f.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Folder>().WithMany().HasForeignKey(f => f.ParentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(f => f.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
