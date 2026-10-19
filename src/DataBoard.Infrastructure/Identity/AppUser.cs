using Microsoft.AspNetCore.Identity;

namespace DataBoard.Infrastructure.Identity;

public sealed class AppUser : IdentityUser<Guid>
{
    public const int MaxDisplayNameLength = 100;

    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }

    /// <summary>A temporary account made by "Continue as guest". It and its sandbox are deleted after <see cref="GuestExpiresUtc"/>.</summary>
    public bool IsGuest { get; set; }
    public DateTime? GuestExpiresUtc { get; set; }
}
