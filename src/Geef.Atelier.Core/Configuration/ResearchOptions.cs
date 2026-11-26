namespace Geef.Atelier.Core.Configuration;

/// <summary>Whether the first run of an order looks up the research topics of the clarifier on the web.</summary>
public enum ResearchMode { Off = 0, On = 1 }

/// <summary>Switches and limits of the research before the first run of an order (section <c>Research</c>).</summary>
public sealed class ResearchOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Research";

    /// <summary>Mode for every order on the direct path.</summary>
    public ResearchMode Mode { get; set; } = ResearchMode.Off;

    /// <summary>Mode per user name, overriding <see cref="Mode"/>.</summary>
    public Dictionary<string, ResearchMode> ModeOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Research topics sent to the search at most; further topics are counted as over the limit.</summary>
    public int MaxSearches { get; set; } = 4;

    /// <summary>Results asked for per search.</summary>
    public int ResultsPerSearch { get; set; } = 3;

    /// <summary>Characters of the raw page text kept per result.</summary>
    public int RawCharsPerPage { get; set; } = 6_000;

    /// <summary>Hard limit of one search; a search is never repeated.</summary>
    public TimeSpan SearchTimeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Hard limit of the one call that picks the passages.</summary>
    public TimeSpan CondenseTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Entries kept in the research of an order at most.</summary>
    public int MaxEntries { get; set; } = 12;

    /// <summary>
    /// Orders that may search per UTC day; later orders get the daily-limit status without a search. The search is reserved before it
    /// starts, so a cancelled search counts as well.
    /// </summary>
    public int DailyOrderCap { get; set; } = 50;

    /// <summary>Orders of one account that may search per UTC day, within <see cref="DailyOrderCap"/>.</summary>
    public int DailyOrderCapPerUser { get; set; } = 20;

    /// <summary>Local directory for raw texts and entries during the acceptance; empty writes nothing.</summary>
    public string? AuditDirectory { get; set; }
}

/// <summary>Helpers on <see cref="ResearchOptions"/>.</summary>
public static class ResearchOptionsExtensions
{
    /// <summary>
    /// The research mode for orders of <paramref name="user"/>: the entry of <see cref="ResearchOptions.ModeOverrides"/> for that user
    /// name, otherwise <see cref="ResearchOptions.Mode"/>.
    /// </summary>
    public static ResearchMode ModeFor(this ResearchOptions options, string? user)
    {
        ArgumentNullException.ThrowIfNull(options);
        return user is not null && options.ModeOverrides is { } overrides && overrides.TryGetValue(user, out var mode) ? mode : options.Mode;
    }
}
