namespace DataBoard.Infrastructure.Identity;

/// <summary>Only the SHA-256 hash of the token is stored, so a database leak does not expose usable tokens.</summary>
public sealed class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }

    public bool IsActive(DateTime nowUtc) => RevokedUtc is null && ExpiresUtc > nowUtc;
}
