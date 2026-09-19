using DataBoard.Domain.Common;

namespace DataBoard.Domain.Datasets;

/// <summary>A folder that organizes datasets. Folders nest; a null parent means the top level.</summary>
public sealed class DatasetFolder : BaseEntity
{
    public const int MaxNameLength = 100;

    /// <summary>The most levels of folders one inside another, the top level folder being level 1.</summary>
    public const int MaxDepth = 8;

    private DatasetFolder() { }

    public Guid OrganizationId { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public static DatasetFolder Create(Guid organizationId, Guid? parentId, string name, Guid createdByUserId, DateTime nowUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(createdByUserId));

        return new DatasetFolder
        {
            OrganizationId = organizationId,
            ParentId = parentId,
            Name = CheckName(name),
            CreatedByUserId = createdByUserId,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
    }

    public void Rename(string name, DateTime nowUtc)
    {
        Name = CheckName(name);
        UpdatedUtc = nowUtc;
    }

    public void MoveTo(Guid? parentId, DateTime nowUtc)
    {
        if (parentId == Id)
            throw new ArgumentException("A folder cannot be inside itself.", nameof(parentId));
        ParentId = parentId;
        UpdatedUtc = nowUtc;
    }

    private static string CheckName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} characters.", nameof(name));
        if (trimmed.Contains('/') || trimmed.Contains('\\'))
            throw new ArgumentException("Name cannot contain / or \\.", nameof(name));
        return trimmed;
    }
}
