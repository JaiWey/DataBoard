using DataBoard.Domain.Folders;
using DataBoard.Domain.DataSources;
using DataBoard.Domain.Datasets;
using DataBoard.Domain.Organizations;
using DataBoard.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DatasetConfiguration : IEntityTypeConfiguration<Dataset>
{
    public void Configure(EntityTypeBuilder<Dataset> builder)
    {
        builder.ToTable("datasets");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(Dataset.MaxNameLength);
        builder.Property(d => d.Description).HasMaxLength(Dataset.MaxDescriptionLength);
        builder.Property(d => d.Sql).IsRequired();
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20).HasDefaultValue(DatasetKind.Sql).HasSentinel(DatasetKind.Sql);
        builder.Property(d => d.DefinitionJson).HasColumnName("definition").HasColumnType("jsonb");
        builder.Property(d => d.ParametersJson).HasColumnName("parameters").HasColumnType("jsonb");
        builder.Property(d => d.CacheSeconds).HasColumnName("cache_seconds").HasDefaultValue(0).HasSentinel(0);

        // The column list is one JSON document per dataset: it is always read and written as a whole.
        builder.OwnsMany(d => d.Columns, columns =>
        {
            columns.ToJson("columns");
            columns.Property(c => c.Role).HasConversion<string>();
        });

        builder.HasIndex(d => new { d.OrganizationId, d.Name }).IsUnique();
        builder.HasIndex(d => d.DataSourceId);
        builder.HasIndex(d => d.FolderId);

        builder.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<DataSource>().WithMany().HasForeignKey(d => d.DataSourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Folder>().WithMany().HasForeignKey(d => d.FolderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(d => d.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
