using Microsoft.AspNetCore.Identity;

namespace DataBoard.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public const int MaxDisplayNameLength = 100;

    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
}
