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
/// <param name="MaxQueriesPerRun">Safety cap on provider calls per run. <c>null</c> means 1 (default).</param>
/// <param name="IsSystem">True for code-constant profiles defined in <see cref="SystemCrew"/>.</param>
public sealed record GroundingProviderProfile(
    string Name,
    string DisplayName,
    string Description,
    string ProviderType,
    Dictionary<string, string> ProviderSettings,
    int? MaxQueriesPerRun,
    bool IsSystem)
{
    public const string KeyRefinementProvider     = "refinementProvider";
    public const string KeyRefinementModel        = "refinementModel";
    public const string KeyRefinementMaxTokens    = "refinementMaxTokens";
    public const string KeyRefinementTemperature  = "refinementTemperature";
    public const string KeyRefinementMode         = "refinementMode";
    public const string KeyRefinementInstructions = "refinementInstructions";

    public const string KeyStaticContent    = "content";
    public const string KeyStaticLabel      = "label";

    public const string KeyUrls              = "urls";
    public const string KeyMaxContentPerUrl  = "maxContentPerUrl";
    public const string KeyStripBoilerplate  = "stripBoilerplate";

    public const string KeyRecencyDays       = "recencyDays";
    public const string KeyNewsMaxResults    = "newsMaxResults";
    public const string KeyNewsSearchDepth   = "newsSearchDepth";

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

    public int RecencyDays =>
        ProviderSettings.TryGetValue(KeyRecencyDays, out var v) && int.TryParse(v, out var n) ? n : 7;

    public int NewsMaxResults =>
        ProviderSettings.TryGetValue(KeyNewsMaxResults, out var v) && int.TryParse(v, out var n) ? n : 5;

    public string? NewsSearchDepth =>
        ProviderSettings.TryGetValue(KeyNewsSearchDepth, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    // ── Academic-search settings keys ────────────────────────────────────────
    public const string KeyAcademicSource    = "source";
    public const string KeyAcademicMaxPapers = "maxPapers";
    public const string KeyAcademicDateFrom  = "dateFrom";
    public const string KeyAcademicFields    = "fields";
    public const string KeyAcademicApiKeyEnv = "apiKeyEnv";

    public string AcademicSource =>
        ProviderSettings.TryGetValue(KeyAcademicSource, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "semantic-scholar";

    public int AcademicMaxPapers =>
        ProviderSettings.TryGetValue(KeyAcademicMaxPapers, out var v) && int.TryParse(v, out var n) ? n : 5;

    public string? AcademicDateFrom =>
        ProviderSettings.TryGetValue(KeyAcademicDateFrom, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public string? AcademicFields =>
        ProviderSettings.TryGetValue(KeyAcademicFields, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public string? AcademicApiKeyEnv =>
        ProviderSettings.TryGetValue(KeyAcademicApiKeyEnv, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    // ── REST-API settings keys ────────────────────────────────────────────────
    public const string KeyRestApiUrl             = "url";
    public const string KeyRestApiMethod          = "method";
    public const string KeyRestApiHeaders         = "headers";
    public const string KeyRestApiBodyTemplate    = "bodyTemplate";
    public const string KeyRestApiResponsePath    = "responsePath";
    public const string KeyRestApiMaxItems        = "maxItems";
    public const string KeyRestApiAuthHeaderEnv   = "authHeaderEnv";
    public const string KeyRestApiAuthHeaderName  = "authHeaderName";
    public const string KeyRestApiAuthHeaderFormat = "authHeaderFormat";

    public string? RestApiUrl =>
        ProviderSettings.TryGetValue(KeyRestApiUrl, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public string RestApiMethod =>
        ProviderSettings.TryGetValue(KeyRestApiMethod, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToUpperInvariant() : "GET";

    public IReadOnlyDictionary<string, string> RestApiHeaders
    {
        get
        {
            if (!ProviderSettings.TryGetValue(KeyRestApiHeaders, out var raw) || string.IsNullOrWhiteSpace(raw))
                return new Dictionary<string, string>();
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(raw)
                       ?? new Dictionary<string, string>();
            }
            catch { return new Dictionary<string, string>(); }
        }
    }

    public string? RestApiBodyTemplate =>
        ProviderSettings.TryGetValue(KeyRestApiBodyTemplate, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public string? RestApiResponsePath =>
        ProviderSettings.TryGetValue(KeyRestApiResponsePath, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public int RestApiMaxItems =>
        ProviderSettings.TryGetValue(KeyRestApiMaxItems, out var v) && int.TryParse(v, out var n) ? n : 10;

    public string? RestApiAuthHeaderEnv =>
        ProviderSettings.TryGetValue(KeyRestApiAuthHeaderEnv, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;

    public string RestApiAuthHeaderName =>
        ProviderSettings.TryGetValue(KeyRestApiAuthHeaderName, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "Authorization";

    public string RestApiAuthHeaderFormat =>
        ProviderSettings.TryGetValue(KeyRestApiAuthHeaderFormat, out var v) && !string.IsNullOrWhiteSpace(v) ? v : "Bearer {token}";
}
