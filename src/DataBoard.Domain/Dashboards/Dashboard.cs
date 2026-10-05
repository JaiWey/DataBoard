using DataBoard.Domain.Common;
using DataBoard.Domain.Organizations;

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
    /// <summary>The folder it is filed in, or null for the top level.</summary>
    public Guid? FolderId { get; private set; }

    // ---- background picture ----

    public const int MaxBackgroundKeyLength = 60;
    public static readonly string[] BackgroundFits = ["cover", "contain", "tile", "center"];

    /// <summary>The key of the picture behind the dashboard, as its store knows it; null when there is none.</summary>
    public string? BackgroundImageKey { get; private set; }

    /// <summary>How the picture fills the dashboard: one of <see cref="BackgroundFits"/>.</summary>
    public string BackgroundFit { get; private set; } = "cover";

    /// <summary>How strong the picture is, 10 to 100 percent (a fainter picture keeps charts readable).</summary>
    public int BackgroundOpacity { get; private set; } = 100;

    /// <summary>
    /// Puts a picture behind the dashboard, or changes how it is shown. A look of the dashboard, not an edit of its content,
    /// so it does not count as an update.
    /// </summary>
    public void SetBackground(string key, string fit, int opacity)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > MaxBackgroundKeyLength)
            throw new ArgumentException("A background needs a picture.", nameof(key));
        if (!BackgroundFits.Contains(fit))
            throw new ArgumentException($"The picture is shown as: {string.Join(", ", BackgroundFits)}.", nameof(fit));
        if (opacity is < 10 or > 100)
            throw new ArgumentException("The picture can be 10 to 100 percent strong.", nameof(opacity));

        BackgroundImageKey = key;
        BackgroundFit = fit;
        BackgroundOpacity = opacity;
    }

    public void ClearBackground()
    {
        BackgroundImageKey = null;
        BackgroundFit = "cover";
        BackgroundOpacity = 100;
    }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    // ---- scheduled refresh: the server re-runs the dashboard's charts now and then, so viewers find fresh data waiting ----

    public const int MinRefreshMinutes = 5;
    public const int MaxRefreshMinutes = 1_440; // a day
    public const int MaxRefreshMessageLength = 300;

    /// <summary>How often the server refreshes the dashboard's charts, in minutes. Null means it does not.</summary>
    public int? RefreshIntervalMinutes { get; private set; }

    /// <summary>When the next refresh is due. Null when there is no schedule.</summary>
    public DateTime? NextRefreshUtc { get; private set; }

    public DateTime? LastRefreshUtc { get; private set; }

    /// <summary>How many charts could not be refreshed the last time.</summary>
    public int LastRefreshFailures { get; private set; }

    /// <summary>The first problem of the last refresh, shortened; null when it went well.</summary>
    public string? LastRefreshMessage { get; private set; }

    /// <summary>
    /// Turns the schedule on, with the first refresh due now, or off (null). Changing the interval keeps the record of
    /// the last refresh. This is a setting of how the dashboard is served, not an edit of the dashboard.
    /// </summary>
    public void SetRefreshInterval(int? minutes, DateTime nowUtc)
    {
        if (minutes is { } m && (m < MinRefreshMinutes || m > MaxRefreshMinutes))
            throw new ArgumentException($"A dashboard refreshes every {MinRefreshMinutes} to {MaxRefreshMinutes} minutes.", nameof(minutes));

        RefreshIntervalMinutes = minutes;
        NextRefreshUtc = minutes is null ? null : nowUtc;
    }

    /// <summary>Notes how a refresh went and, if there is still a schedule, when the next one is due.</summary>
    public void RecordRefresh(DateTime nowUtc, int failedCharts, string? message)
    {
        if (failedCharts < 0)
            throw new ArgumentOutOfRangeException(nameof(failedCharts));

        LastRefreshUtc = nowUtc;
        LastRefreshFailures = failedCharts;
        LastRefreshMessage = failedCharts == 0 || string.IsNullOrWhiteSpace(message)
            ? null
            : message.Trim().Length > MaxRefreshMessageLength ? message.Trim()[..(MaxRefreshMessageLength - 1)] + "…" : message.Trim();
        if (RefreshIntervalMinutes is { } minutes) NextRefreshUtc = nowUtc.AddMinutes(minutes);
    }

    public static Dashboard Create(
        Guid organizationId, string name, string? description, string definitionJson, Guid createdByUserId, DateTime nowUtc,
        Guid? folderId = null)
    {
        if (organizationId == Guid.Empty)
            throw new ArgumentException("Organization is required.", nameof(organizationId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(createdByUserId));

        var dashboard = new Dashboard { OrganizationId = organizationId, CreatedByUserId = createdByUserId, CreatedUtc = nowUtc, FolderId = folderId };
        dashboard.Update(name, description, definitionJson, nowUtc);
        return dashboard;
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

    /// <summary>Filing a dashboard is organization, not a change to the dashboard, so it does not count as an update.</summary>
    public void MoveTo(Guid? folderId) => FolderId = folderId;

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
