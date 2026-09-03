using DataBoard.Domain.Common;

namespace DataBoard.Domain.Organizations;

public sealed class Organization : BaseEntity
{
    public const int MaxNameLength = 200;

    private Organization() { }

    public string Name { get; private set; } = string.Empty;
    public DateTime CreatedUtc { get; private set; }

    public static Organization Create(string name, DateTime createdUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} characters.", nameof(name));

        return new Organization { Name = name.Trim(), CreatedUtc = createdUtc };
    }
}
