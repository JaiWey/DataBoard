using DataBoard.Domain.Common;

namespace DataBoard.Domain.Organizations;

public sealed class Organization : BaseEntity
{
    public const int MaxNameLength = 200;
    public const string DefaultName = "Default";

    private Organization() { }

    public string Name { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }

    /// <summary>The one organization every user belongs to from the moment they sign up. It cannot be left or removed.</summary>
    public bool IsDefault { get; private set; }

    public static Organization Create(string name, DateTime createdUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} characters.", nameof(name));

        return new Organization { Name = name.Trim(), CreatedUtc = createdUtc };
    }

    public static Organization CreateDefault(DateTime createdUtc) =>
        new() { Name = DefaultName, CreatedUtc = createdUtc, IsDefault = true };
}
