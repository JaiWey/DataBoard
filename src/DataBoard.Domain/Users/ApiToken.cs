using DataBoard.Domain.Common;

namespace DataBoard.Domain.Users;

/// <summary>What a personal access token may do through the MCP endpoint.</summary>
public enum ApiTokenScope
{
    /// <summary>List and read datasets, charts and dashboards, and run chart queries.</summary>
    Read,

    /// <summary>Everything in <see cref="Read"/>, and create or change charts and dashboards. Never delete.</summary>
    Write,
}

/// <summary>
/// A personal access token: how a tool such as an AI assistant acts as its owner, with no password. It carries the
/// owner's organization roles (it can never do more than they can) and is limited further by its scope. Only a hash of
/// the secret is stored, so a copy of the database does not hand out working tokens.
/// </summary>
public sealed class ApiToken : BaseEntity
{
    public const int MaxNameLength = 100;
    public const int TokenHashLength = 64; // SHA-256 as hex
    public const int MaxPerUser = 20;
    public const int MaxLifetimeDays = 365;

    private ApiToken() { }

    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;
    public ApiTokenScope Scope { get; private set; }
    public DateTime CreatedUtc { get; private set; }

    /// <summary>Null means the token never expires (until it is revoked).</summary>
    public DateTime? ExpiresUtc { get; private set; }

    public DateTime? LastUsedUtc { get; private set; }

    public static ApiToken Create(
        Guid userId, string name, string tokenHash, ApiTokenScope scope, DateTime nowUtc, DateTime? expiresUtc)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(userId));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} characters.", nameof(name));
        if (tokenHash is null || tokenHash.Length != TokenHashLength)
            throw new ArgumentException("A token hash is required.", nameof(tokenHash));
        if (!Enum.IsDefined(scope))
            throw new ArgumentException("Unknown scope.", nameof(scope));
        if (expiresUtc is { } expires && expires <= nowUtc)
            throw new ArgumentException("A token has to expire in the future.", nameof(expiresUtc));

        return new ApiToken
        {
            UserId = userId,
            Name = name.Trim(),
            TokenHash = tokenHash,
            Scope = scope,
            CreatedUtc = nowUtc,
            ExpiresUtc = expiresUtc,
        };
    }

    public bool IsActive(DateTime nowUtc) => ExpiresUtc is null || ExpiresUtc > nowUtc;

    /// <summary>Records a use, at most once in a while, so a busy client does not write to the database on every call.</summary>
    public bool MarkUsed(DateTime nowUtc)
    {
        if (LastUsedUtc is { } last && nowUtc - last < TimeSpan.FromMinutes(5)) return false;
        LastUsedUtc = nowUtc;
        return true;
    }
}
