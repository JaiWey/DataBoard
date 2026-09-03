namespace DataBoard.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";
    public const int MinKeyBytes = 32;

    public string Issuer { get; set; } = "DataBoard";
    public string Audience { get; set; } = "DataBoard";

    /// <summary>Symmetric signing key. Must come from user secrets or the environment, never from source control.</summary>
    public string Key { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;

    public byte[] KeyBytes()
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(Key ?? string.Empty);
        if (bytes.Length < MinKeyBytes)
            throw new InvalidOperationException(
                $"Jwt:Key must be at least {MinKeyBytes} bytes. Set it with user secrets or an environment variable.");
        return bytes;
    }
}
