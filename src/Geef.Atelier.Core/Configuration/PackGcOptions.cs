namespace Geef.Atelier.Core.Configuration;

/// <summary>Configuration for the specialization-pack auto-archival (garbage-collection) background service.</summary>
public sealed class PackGcOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "PackGc";

    /// <summary>Whether auto-archival runs at all. Default true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the archival sweep runs, in hours. Default 24.</summary>
    public int IntervalHours { get; set; } = 24;

    /// <summary>
    /// Packs unused for at least this many days (and not referenced by any template) are archived.
    /// Default 90.
    /// </summary>
    public int RetentionDays { get; set; } = 90;
}
