namespace Geef.Atelier.Web.Resources;

/// <summary>Field help texts displayed below each field in the Grounding Provider Editor.</summary>
public static class GroundingFieldHelps
{
    public const string RefinementEnabled = "Enable AI refinement: the refiner filters raw results before they are appended to the briefing.";
    public const string RefinementProvider = "The LLM provider for the refinement. Cheap models are often sufficient (e.g. Gemini Flash).";
    public const string RefinementModel = "The model for the refinement. Fast, low-cost models are sufficient for filtering tasks.";
    public const string RefinementMaxTokens = "Maximum number of tokens for the refinement response. At least 256.";
    public const string RefinementTemperature = "Creativity level: empty = provider default, 0.0 = deterministic, 2.0 = very creative. Recommended for filtering: 0.0.";
    public const string RefinementMode = "Filter: sources are kept or discarded. Synthesize: all sources are merged into one coherent text.";
    public const string RefinementInstructions = "Optional additional instructions for the refiner (e.g. 'Discard all sources without a 2025/2026 date').";

    // Static-context
    public const string StaticContextLabel =
        "Source name for attribution (e.g. \"Brand voice\", \"Glossary Q2 2026\"). Appears in the grounding visualization.";

    public const string StaticContextContent =
        "The curated text that is inserted unchanged into the context on every run. " +
        "Soft limit 50,000 characters — very large texts (>50k) belong in the knowledge base " +
        "(use a vector-store provider instead).";

    // URL-fetch
    public const string UrlFetchUrls =
        "One URL per line, https:// or http:// only. " +
        "Important: only publicly reachable URLs — internal addresses " +
        "(localhost, 127.0.0.1, 10.x, 192.168.x, cloud metadata) are blocked for security reasons. " +
        "JavaScript-heavy pages may return little content (no browser rendering).";

    public const string UrlFetchMaxContent =
        "Maximum number of characters per URL after HTML cleanup. Default 8,000. " +
        "Higher values increase context consumption.";

    public const string UrlFetchStripBoilerplate =
        "Removes navigation, ads, cookie banners and similar boilerplate elements during parsing. " +
        "Recommended: enabled. The AI refiner can additionally filter out residual clutter.";
}
