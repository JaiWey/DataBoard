namespace DataBoard.Infrastructure.Identity;

/// <summary>Settings of "Continue as guest" (the "Guests" section of the configuration).</summary>
public sealed class GuestOptions
{
    public const string SectionName = "Guests";

    /// <summary>Off unless turned on, so an existing deployment does not suddenly let anyone in.</summary>
    public bool Enabled { get; set; }

    /// <summary>How long a guest and their sandbox last.</summary>
    public int LifetimeHours { get; set; } = 24;

    /// <summary>The most guests alive at once, so the button cannot be used to fill the database.</summary>
    public int MaxActive { get; set; } = 200;
}
