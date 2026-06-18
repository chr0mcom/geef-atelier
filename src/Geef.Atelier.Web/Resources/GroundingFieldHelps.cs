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

    // News-search
    public const string NewsSearchRecencyDays =
        "Only news from the last N days. Default 7, maximum 365.";

    public const string NewsSearchMaxResults =
        "Maximum number of news hits per request. Default 5, maximum 20.";

    public const string NewsSearchDepth =
        "Tavily search depth: basic (faster, cheaper) or advanced (more thorough, higher credit cost).";

    // Academic-search
    public const string AcademicSource =
        "Academic API: arXiv (preprints CS/physics/math, no key), " +
        "Semantic Scholar (broad coverage, optional key for higher rate limits), " +
        "OpenAlex (very broad, modern, free).";

    public const string AcademicMaxPapers =
        "Maximum number of papers per request. Default 5, recommended 3–10.";

    public const string AcademicDateFrom =
        "Only papers from this date onwards (ISO format: YYYY or YYYY-MM-DD). Empty = no date filter.";

    public const string AcademicFields =
        "Optional search-field restriction (e.g. 'ti' for title on arXiv). Empty = all fields.";

    public const string AcademicApiKeyEnv =
        "ENV variable name for the Semantic Scholar API key (e.g. SEMANTIC_SCHOLAR_API_KEY). " +
        "Important: enter the variable name here, not the key itself! Empty = no key (lower rate limits).";

    // REST-API
    public const string RestApiUrl =
        "Full URL of the endpoint (https://…). The placeholder {briefing} is replaced by the URL-encoded briefing. " +
        "Security note: internal addresses (localhost, 10.x, 192.168.x, 172.16–31.x, 169.254.x) are blocked for " +
        "security reasons (SSRF protection). Only JSON responses are supported.";

    public const string RestApiMethod =
        "HTTP method: GET (default, parameters via URL) or POST (data in the body).";

    public const string RestApiHeaders =
        "Additional HTTP headers as a JSON object, e.g. {\"Accept\": \"application/json\"}. " +
        "Do not store secrets here — use the authentication ENV variable instead.";

    public const string RestApiBodyTemplate =
        "Request body template for POST requests. {briefing} is replaced by the JSON-escaped briefing. " +
        "Example: {\"query\": \"{briefing}\", \"limit\": 10}";

    public const string RestApiResponsePath =
        "JSONPath to the relevant part of the response, e.g. $.results or $.data[*].content. " +
        "Empty = use the entire response.";

    public const string RestApiMaxItems =
        "Maximum number of extracted array items. Default 10.";

    public const string RestApiAuthHeaderEnv =
        "ENV variable name for the auth token (e.g. MY_API_TOKEN). " +
        "Important: enter the variable name here, not the token itself! " +
        "The token is read from the environment variable at runtime and never appears in the database.";

    public const string RestApiAuthHeaderName =
        "Name of the auth header. Default: Authorization. Other examples: X-Api-Key, Token.";

    public const string RestApiAuthHeaderFormat =
        "Format of the header value; {token} is replaced by the resolved token. Default: Bearer {token}.";
}
