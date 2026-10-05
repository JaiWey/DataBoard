using DataBoard.Domain.Common;
using DataBoard.Domain.Organizations;

namespace DataBoard.Domain.Folders;

/// <summary>What a folder organizes. Each kind has its own tree, so a dataset folder never holds a chart.</summary>
public enum FolderKind
{
    Dataset = 1,
    Chart = 2,
    Dashboard = 3,
}

/// <summary>A folder that organizes datasets, charts or dashboards. Folders nest; a null parent means the top level.</summary>
public sealed class Folder : BaseEntity
{
    public const int MaxNameLength = 100;

    /// <summary>The most levels of folders one inside another, the top level folder being level 1.</summary>
    public const int MaxDepth = 8;

    private Folder() { }

    public Guid OrganizationId { get; private set; }
    public FolderKind Kind { get; private set; }
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public static Folder Create(Guid organizationId, FolderKind kind, Guid? parentId, string name, Guid createdByUserId, DateTime nowUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(createdByUserId));
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("Folder kind is not valid.", nameof(kind));

        return new Folder
        {
            OrganizationId = organizationId,
            Kind = kind,
            ParentId = parentId,
            Name = CheckName(name),
            CreatedByUserId = createdByUserId,
            CreatedUtc = nowUtc,
            UpdatedUtc = nowUtc,
        };
    }

    // ---- who may see it ----

    /// <summary>Null, together with an empty <see cref="AccessMemberIds"/>, means everyone in the organization.</summary>
    public OrganizationRole? AccessMinRole { get; private set; }
    public Guid[] AccessMemberIds { get; private set; } = [];

    public AccessRule Access => new(AccessMinRole, AccessMemberIds);

    /// <summary>Who may see it. Changing this is not an edit of what it holds, so it does not count as an update.</summary>
    public void SetAccess(AccessRule rule)
    {
        var checkedRule = AccessRule.Create(rule.MinRole, rule.MemberIds);
        AccessMinRole = checkedRule.MinRole;
        AccessMemberIds = [.. checkedRule.MemberIds];
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
