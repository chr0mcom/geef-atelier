using Geef.Atelier.Core.Domain.FactCheck;

namespace Geef.Atelier.Core.Configuration;

/// <summary>Switches of the direct path (section <c>Direct</c>).</summary>
public sealed class DirectOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Direct";

    /// <summary>Whether the writer appends the block with its assumptions, open points and quotes after the text.</summary>
    public bool WriterNotes { get; set; } = true;

    /// <summary>Whether totals in the files are recalculated without an AI model.</summary>
    public bool SourceArithmetic { get; set; } = true;

    /// <summary>How the fact check runs for every order on the direct path.</summary>
    public FactCheckMode FactCheckMode { get; set; } = FactCheckMode.Report;

    /// <summary>Mode per user name, overriding <see cref="FactCheckMode"/>.</summary>
    public Dictionary<string, FactCheckMode> FactCheckModeOverrides { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Material and text above this size are checked without the content of the files.</summary>
    public int FactCheckMaxMaterialChars { get; set; } = 150_000;

    /// <summary>
    /// Hard limit of one call of the fact-check model. 90 s: the longest successful call of the calibration took 86 s, and a call
    /// cut off too early only costs the one call of the fallback model (decision log D-073).
    /// </summary>
    public TimeSpan FactCheckTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Hard limit of the one call of the fallback model after the fact-check model failed or ran into its limit.</summary>
    public TimeSpan FactCheckFallbackTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Output limit of the fact-check call; a safety cap, not a target.</summary>
    public int FactCheckMaxTokens { get; set; } = 8_192;

    /// <summary>Reasoning effort sent with the fact-check call; <c>null</c> sends none.</summary>
    public string? FactCheckReasoningEffort { get; set; }

    /// <summary>Whether a failed fact-check call is tried once more with the next model of the slot.</summary>
    public bool FactCheckFallbackModel { get; set; } = true;
}

/// <summary>Helpers on <see cref="DirectOptions"/>.</summary>
public static class DirectOptionsExtensions
{
    /// <summary>
    /// The fact-check mode for orders of <paramref name="user"/>: the entry of <see cref="DirectOptions.FactCheckModeOverrides"/> for
    /// that user name, otherwise <see cref="DirectOptions.FactCheckMode"/>.
    /// </summary>
    public static FactCheckMode FactCheckModeFor(this DirectOptions options, string? user)
    {
        ArgumentNullException.ThrowIfNull(options);
        return user is not null && options.FactCheckModeOverrides is { } overrides && overrides.TryGetValue(user, out var mode) ? mode : options.FactCheckMode;
    }
}
