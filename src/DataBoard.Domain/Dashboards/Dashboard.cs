using DataBoard.Domain.Common;

namespace DataBoard.Domain.Dashboards;

/// <summary>
/// A named page of charts. The widgets and shared filters are one JSON document: it is always read and written
/// as a whole, and its shape is owned and validated by the application layer (it refers to chart queries).
/// </summary>
public sealed class Dashboard : BaseEntity
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 1000;

    private Dashboard() { }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public string DefinitionJson { get; private set; } = "{}";
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public static Dashboard Create(
        Guid organizationId, string name, string? description, string definitionJson, Guid createdByUserId, DateTime nowUtc)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(createdByUserId));

        var dashboard = new Dashboard { OrganizationId = organizationId, CreatedByUserId = createdByUserId, CreatedUtc = nowUtc };
        dashboard.Update(name, description, definitionJson, nowUtc);
        return dashboard;
    }

    public void Update(string name, string? description, string definitionJson, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (name.Trim().Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} characters.", nameof(name));
        if (description is { Length: > MaxDescriptionLength })
            throw new ArgumentException($"Description must be at most {MaxDescriptionLength} characters.", nameof(description));
        if (string.IsNullOrWhiteSpace(definitionJson))
            throw new ArgumentException("Definition is required.", nameof(definitionJson));

        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        DefinitionJson = definitionJson;
        UpdatedUtc = nowUtc;
    }
}
