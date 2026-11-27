using System.Globalization;
using Geef.Atelier.Core.Domain.Llm;

namespace Geef.Atelier.Core.Domain.Crew.Grounding;

/// <summary>
/// Describes how a single grounding provider should be invoked for a run.
/// System profiles are code constants; custom profiles are persisted in the database
/// under the <c>"custom-"</c> name prefix.
/// </summary>
/// <param name="Name">Unique identifier. System profiles use plain names; custom profiles carry a <c>"custom-"</c> prefix.</param>
/// <param name="DisplayName">Human-readable label surfaced in the UI.</param>
/// <param name="Description">One- or two-sentence summary of the provider's purpose.</param>
/// <param name="ProviderType">Discriminator string used by <c>IGroundingProviderFactory</c> (e.g. <c>"tavily"</c>).</param>
/// <param name="ProviderSettings">
/// Provider-specific configuration as string key/value pairs.
/// For Tavily: <c>Tier</c> (basic|advanced), <c>MaxResults</c>, <c>IncludeAnswer</c>.
/// Designed to be extensible for a future <c>VectorStoreGroundingProvider</c> without a schema change.
/// </param>
/// <param name="IsSystem">True for code-constant profiles defined in <see cref="SystemCrew"/>.</param>
/// <param name="ToolName">
/// Optional reference to a <c>ToolDefinition.Name</c> that backs this provider.
/// When set, <c>MultiProviderGroundingStep</c> uses <c>ToolBackedGroundingProvider</c> instead of
/// the legacy <c>ProviderType</c>-based factory path.  Backward-compatible: existing profiles
/// without a <c>ToolName</c> continue to use the factory path unchanged.
/// </param>
public sealed record GroundingProviderProfile(
    string Name,
    string DisplayName,
    string Description,
    string ProviderType,
    Dictionary<string, string> ProviderSettings,
    bool IsSystem,
    string? ToolName = null)
{
    public const string KeyRefinementProvider     = "refinementProvider";
    public const string KeyRefinementModel        = "refinementModel";
    public const string KeyRefinementMaxTokens    = "refinementMaxTokens";
    public const string KeyRefinementTemperature  = "refinementTemperature";
    public const string KeyRefinementMode         = "refinementMode";
    public const string KeyRefinementInstructions = "refinementInstructions";

    /// <summary>Settings key marking the refiner binding as managed by the model matrix (value: a <see cref="Models.ModelSlot"/> name).</summary>
    public const string KeyModelSlot = "modelSlot";

    public const string KeyStaticContent    = "content";
    public const string KeyStaticLabel      = "label";

    public const string KeyUrls              = "urls";
    public const string KeyMaxContentPerUrl  = "maxContentPerUrl";
    public const string KeyStripBoilerplate  = "stripBoilerplate";


    /// <summary>
    /// Model-matrix slot of the refiner when the binding is managed (<see cref="KeyModelSlot"/>); <c>null</c> when
    /// the refiner was configured manually or no refinement is configured.
    /// </summary>
    public Models.ModelSlot? RefinementModelSlot =>
        RefinementBinding is null
            ? null
            : Models.ModelSlotParser.ParseOrNull(ProviderSettings.GetValueOrDefault(KeyModelSlot)) is Models.ModelSlot.GroundingRefiner
                ? Models.ModelSlot.GroundingRefiner
                : null;

    /// <summary>
    /// Returns a configured <see cref="LlmBinding"/> when all mandatory refinement keys are present,
    /// or <c>null</c> when no refinement is configured for this profile.
    /// </summary>
    public LlmBinding? RefinementBinding
    {
        get
        {
            var provider = ProviderSettings.GetValueOrDefault(KeyRefinementProvider);
            var model    = ProviderSettings.GetValueOrDefault(KeyRefinementModel);
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
                return null;
            var maxTokens = int.TryParse(ProviderSettings.GetValueOrDefault(KeyRefinementMaxTokens), out var mt) ? mt : 2048;
            double? temperature = double.TryParse(ProviderSettings.GetValueOrDefault(KeyRefinementTemperature),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var t) ? t : null;
            return new LlmBinding(provider, model, maxTokens, temperature);
        }
    }

    /// <summary>
    /// The refinement mode to apply; defaults to <see cref="GroundingRefinementMode.Filter"/>
    /// when the key is absent or unparseable.
    /// </summary>
    public GroundingRefinementMode RefinementMode
    {
        get
        {
            if (int.TryParse(ProviderSettings.GetValueOrDefault(KeyRefinementMode), out var m))
                return (GroundingRefinementMode)m;
            return GroundingRefinementMode.Filter;
        }
    }

    /// <summary>Optional free-text instructions for the refinement prompt; <c>null</c> when not set.</summary>
    public string? RefinementInstructions =>
        ProviderSettings.TryGetValue(KeyRefinementInstructions, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public string? StaticContent =>
        ProviderSettings.TryGetValue(KeyStaticContent, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public string? StaticLabel =>
        ProviderSettings.TryGetValue(KeyStaticLabel, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public IReadOnlyList<string> Urls
    {
        get
        {
            if (!ProviderSettings.TryGetValue(KeyUrls, out var raw) || string.IsNullOrWhiteSpace(raw))
                return [];
            return raw.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                      .Where(s => !string.IsNullOrWhiteSpace(s))
                      .ToList();
        }
    }

    public int MaxContentPerUrl =>
        ProviderSettings.TryGetValue(KeyMaxContentPerUrl, out var v) && int.TryParse(v, out var n) ? n : 8000;

    public bool StripBoilerplate =>
        !ProviderSettings.TryGetValue(KeyStripBoilerplate, out var v) || !bool.TryParse(v, out var b) || b;


}
