using Geef.Atelier.Core.Domain.Crew.Composition;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Whitelist for the free <c>settings</c> object the crew composer may attach to inline grounding providers and
/// finalizers. Composer output is model text that briefings and grounding sources can steer, so it must never set
/// network targets (including <c>url-fetch</c> URLs), credentials, environment-variable names or model bindings: those
/// come from reused, operator-made profiles or from the model matrix (D-066).
/// </summary>
internal static class CrewPartSettingsPolicy
{
    /// <summary>Grounding provider types that need operator-made targets or credentials and are never accepted inline.</summary>
    public static readonly IReadOnlySet<string> ReuseOnlyGroundingTypes =
        new HashSet<string>(["rest-api"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Grounding provider types whose only purpose is fetching targets the composer may not set (<c>url-fetch</c>): an
    /// inline definition would be an empty profile, so it is dropped with a warning instead of being materialized.
    /// </summary>
    public static readonly IReadOnlySet<string> DroppedInlineGroundingTypes =
        new HashSet<string>(["url-fetch"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Parses a composer finalizer type by name; unknown or numeric values become <see cref="FinalizerType.FileExport"/>.</summary>
    public static FinalizerType ParseFinalizerType(string? raw) =>
        TryParseFinalizerType(raw, out var type) ? type : FinalizerType.FileExport;

    /// <summary>Parses a composer finalizer type by name (trimmed, case-insensitive); numbers and unknown names fail.</summary>
    public static bool TryParseFinalizerType(string? raw, out FinalizerType type)
    {
        FinalizerType? parsed = raw?.Trim().ToLowerInvariant() switch
        {
            "fileexport" => FinalizerType.FileExport,
            "metadataenrich" => FinalizerType.MetadataEnrich,
            "transform" => FinalizerType.Transform,
            "learningextract" => FinalizerType.LearningExtract,
            "learningpublish" => FinalizerType.LearningPublish,
            "crewmaterialize" => FinalizerType.CrewMaterialize,
            _ => null,
        };
        type = parsed ?? FinalizerType.FileExport;
        return parsed is not null;
    }

    /// <summary>True for an inline (non-reuse) finalizer whose parsed type is <see cref="FinalizerType.Transform"/>.</summary>
    public static bool IsInlineTransform(this CrewPartSpec part) =>
        string.IsNullOrWhiteSpace(part.Reuse) && ParseFinalizerType(part.FinalizerType) == FinalizerType.Transform;

    private static readonly HashSet<string> GroundingKeys = new(StringComparer.Ordinal)
    {
        GroundingProviderProfile.KeyRefinementMode,
        GroundingProviderProfile.KeyRefinementMaxTokens,
        GroundingProviderProfile.KeyRefinementInstructions,
        GroundingProviderProfile.KeyStaticContent,
        GroundingProviderProfile.KeyStaticLabel,
        GroundingProviderProfile.KeyMaxContentPerUrl,
        GroundingProviderProfile.KeyStripBoilerplate,
        "Tier",
        "MaxResults",
        "IncludeAnswer",
        "MinRelevanceScore",
        "ExtractQuery",
        "TopK",
        "TagFilter",
        "Scope",
        "domain",
    };

    private static readonly Dictionary<FinalizerType, HashSet<string>> FinalizerKeys = new()
    {
        [FinalizerType.Transform] = new(StringComparer.Ordinal) { TransformSettings.KeySystemPrompt, TransformSettings.KeyTemperature },
        [FinalizerType.FileExport] = new(StringComparer.Ordinal) { FileExportSettings.KeyFormat },
        [FinalizerType.MetadataEnrich] = new(StringComparer.Ordinal) { MetadataEnrichSettings.KeyEnricherType },
    };

    /// <summary>Settings of an inline grounding provider reduced to the whitelisted keys.</summary>
    public static Dictionary<string, string> AllowedGroundingSettings(this CrewPartSpec part) =>
        Filter(part.Settings, GroundingKeys);

    /// <summary>Settings of an inline finalizer of <paramref name="type"/> reduced to the whitelisted keys.</summary>
    public static Dictionary<string, string> AllowedFinalizerSettings(this CrewPartSpec part, FinalizerType type) =>
        Filter(part.Settings, FinalizerKeys.GetValueOrDefault(type) ?? []);

    /// <summary>Keys of <paramref name="part"/>'s settings that the whitelist drops, for validation warnings.</summary>
    public static IReadOnlyList<string> DroppedGroundingKeys(this CrewPartSpec part) =>
        Dropped(part.Settings, GroundingKeys);

    /// <summary>Keys of <paramref name="part"/>'s finalizer settings that the whitelist drops, for validation warnings.</summary>
    public static IReadOnlyList<string> DroppedFinalizerKeys(this CrewPartSpec part, FinalizerType type) =>
        Dropped(part.Settings, FinalizerKeys.GetValueOrDefault(type) ?? []);

    private static Dictionary<string, string> Filter(IReadOnlyDictionary<string, string>? settings, HashSet<string> allowed) =>
        (settings ?? new Dictionary<string, string>())
            .Where(kv => allowed.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

    private static IReadOnlyList<string> Dropped(IReadOnlyDictionary<string, string>? settings, HashSet<string> allowed) =>
        (settings ?? new Dictionary<string, string>()).Keys.Where(k => !allowed.Contains(k)).Order(StringComparer.Ordinal).ToList();
}
