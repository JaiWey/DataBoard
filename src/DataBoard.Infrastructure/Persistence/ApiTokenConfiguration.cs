using DataBoard.Domain.Users;
using DataBoard.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataBoard.Infrastructure.Persistence;

internal sealed class ApiTokenConfiguration : IEntityTypeConfiguration<ApiToken>
{
    public void Configure(EntityTypeBuilder<ApiToken> builder)
    {
        builder.ToTable("api_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(ApiToken.MaxNameLength);
        builder.Property(t => t.TokenHash).IsRequired().HasMaxLength(ApiToken.TokenHashLength).IsFixedLength();
        builder.Property(t => t.Scope).HasConversion<string>().HasMaxLength(16);

        // A token is found by its hash on every call.
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.UserId);

        builder.HasOne<AppUser>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
