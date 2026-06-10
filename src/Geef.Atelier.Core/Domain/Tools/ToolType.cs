namespace Geef.Atelier.Core.Domain.Tools;

/// <summary>Known tool type discriminator strings.</summary>
public static class ToolType
{
    /// <summary>Web search via Tavily (maps to <c>GroundingProviderTypes.Tavily</c>).</summary>
    public const string WebSearch = "web-search";

    /// <summary>Vector-store knowledge-base retrieval (maps to <c>GroundingProviderTypes.VectorStore</c>).</summary>
    public const string KnowledgeBase = "knowledge-base";

    /// <summary>Single-URL fetch and extraction (maps to <c>GroundingProviderTypes.UrlFetch</c>).</summary>
    public const string UrlFetch = "url-fetch";

    /// <summary>News article search (maps to <c>GroundingProviderTypes.NewsSearch</c>).</summary>
    public const string NewsSearch = "news-search";

    /// <summary>Academic paper search via arXiv / SemanticScholar / OpenAlex (maps to <c>GroundingProviderTypes.AcademicSearch</c>).</summary>
    public const string AcademicSearch = "academic-search";

    /// <summary>Generic HTTP REST API call with optional JSONPath extraction (maps to <c>GroundingProviderTypes.RestApi</c>).</summary>
    public const string RestApi = "rest-api";

    /// <summary>Inlined static text context (maps to <c>GroundingProviderTypes.StaticContext</c>).</summary>
    public const string StaticContext = "static-context";

    /// <summary>Retrieval from the run's accumulated learning entries (maps to <c>GroundingProviderTypes.LearningRetrieval</c>).</summary>
    public const string LearningRetrieval = "learning-retrieval";

    /// <summary>Arbitrary MCP tool invocation (new; no direct grounding-provider equivalent).</summary>
    public const string McpTool = "mcp-tool";

    /// <summary>All known tool type strings.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        WebSearch,
        KnowledgeBase,
        UrlFetch,
        NewsSearch,
        AcademicSearch,
        RestApi,
        StaticContext,
        LearningRetrieval,
        McpTool
    ];
}
