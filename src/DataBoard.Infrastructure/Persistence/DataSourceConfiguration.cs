using DataBoard.Domain.DataSources;
using DataBoard.Domain.Organizations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class DataSourceConfiguration : IEntityTypeConfiguration<DataSource>
{
    public void Configure(EntityTypeBuilder<DataSource> builder)
    {
        builder.ToTable("data_sources");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Name).IsRequired().HasMaxLength(DataSource.MaxNameLength);
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.EncryptedConnectionString).IsRequired();
        builder.HasIndex(d => new { d.OrganizationId, d.Name }).IsUnique();
        builder.HasOne<Organization>().WithMany().HasForeignKey(d => d.OrganizationId).OnDelete(DeleteBehavior.Cascade);
    }
}
