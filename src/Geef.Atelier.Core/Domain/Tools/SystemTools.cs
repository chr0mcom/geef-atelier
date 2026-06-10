using System.Text.Json;

namespace Geef.Atelier.Core.Domain.Tools;

/// <summary>Central catalog of system-provided tool capabilities.</summary>
public static class SystemTools
{
    public static readonly ToolDefinition WebSearch = new(
        Name: "web-search",
        DisplayName: "Web Search (Tavily)",
        Description: "Searches the web using the Tavily API. Retrieves current, real-time information from the internet.",
        ToolType: ToolType.WebSearch,
        Settings: new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.ApiKey] = "",  // resolved from TAVILY_API_KEY env var
            [ToolDefinitionSettingsKeys.MaxResults] = "5"
        },
        SecretRef: "TAVILY_API_KEY",
        LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string","description":"Search query"}},"required":["query"]}""").RootElement,
        AccessClass: ToolAccessClass.ReadOnly,
        IsSystem: true
    );

    public static readonly ToolDefinition KnowledgeBase = new(
        Name: "knowledge-base",
        DisplayName: "Knowledge Base (Vector Store)",
        Description: "Searches the project's vector knowledge base using semantic similarity.",
        ToolType: ToolType.KnowledgeBase,
        Settings: new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.CollectionName] = "default",
            ["TopK"] = "5",
            ["Scope"] = "global"
        },
        SecretRef: null,
        LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string","description":"Semantic search query"}},"required":["query"]}""").RootElement,
        AccessClass: ToolAccessClass.ReadOnly,
        IsSystem: true
    );

    public static readonly ToolDefinition UrlFetch = new(
        Name: "url-fetch",
        DisplayName: "URL Fetch",
        Description: "Fetches and extracts readable text content from a given URL. Respects SSRF safety checks.",
        ToolType: ToolType.UrlFetch,
        Settings: new Dictionary<string, string>
        {
            ["maxContentPerUrl"] = "8000"
        },
        SecretRef: null,
        LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{"url":{"type":"string","description":"URL to fetch (must be public)"}},"required":["url"]}""").RootElement,
        AccessClass: ToolAccessClass.ReadOnly,
        IsSystem: true
    );

    public static readonly ToolDefinition NewsSearch = new(
        Name: "news-search",
        DisplayName: "News Search",
        Description: "Searches recent news articles using the Tavily news API.",
        ToolType: ToolType.NewsSearch,
        Settings: new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.MaxResults] = "5",
            ["recencyDays"] = "7"
        },
        SecretRef: "TAVILY_API_KEY",
        LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string","description":"News search query"}},"required":["query"]}""").RootElement,
        AccessClass: ToolAccessClass.ReadOnly,
        IsSystem: true
    );

    public static readonly ToolDefinition AcademicSearch = new(
        Name: "academic-search",
        DisplayName: "Academic Search",
        Description: "Searches academic papers via Semantic Scholar, arXiv, or OpenAlex.",
        ToolType: ToolType.AcademicSearch,
        Settings: new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.AcademicSource] = "semantic-scholar",
            ["maxPapers"] = "5"
        },
        SecretRef: null,
        LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string","description":"Academic search query"}},"required":["query"]}""").RootElement,
        AccessClass: ToolAccessClass.ReadOnly,
        IsSystem: true
    );

    public static readonly ToolDefinition RestApi = new(
        Name: "rest-api",
        DisplayName: "REST API",
        Description: "Calls an external REST API endpoint and extracts data using JSONPath.",
        ToolType: ToolType.RestApi,
        Settings: new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.HttpMethod] = "GET"
        },
        SecretRef: null,
        LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{"endpoint":{"type":"string","description":"API endpoint URL"},"params":{"type":"object","additionalProperties":{"type":"string"},"description":"Query parameters"}},"required":["endpoint"]}""").RootElement,
        AccessClass: ToolAccessClass.ReadOnly,
        IsSystem: true
    );

    public static readonly ToolDefinition StaticContext = new(
        Name: "static-context",
        DisplayName: "Static Context",
        Description: "Provides pre-defined static context text to the pipeline.",
        ToolType: ToolType.StaticContext,
        Settings: new Dictionary<string, string>
        {
            [ToolDefinitionSettingsKeys.StaticContent] = ""
        },
        SecretRef: null,
        LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""").RootElement,
        AccessClass: ToolAccessClass.ReadOnly,
        IsSystem: true
    );

    public static readonly ToolDefinition LearningRetrieval = new(
        Name: "learning-retrieval",
        DisplayName: "Learning Retrieval",
        Description: "Retrieves approved learnings from the project learning store using semantic similarity.",
        ToolType: ToolType.LearningRetrieval,
        Settings: new Dictionary<string, string>
        {
            ["TopK"] = "5"
        },
        SecretRef: null,
        LlmSchema: JsonDocument.Parse("""{"type":"object","properties":{"query":{"type":"string","description":"Learning retrieval query"}},"required":["query"]}""").RootElement,
        AccessClass: ToolAccessClass.ReadOnly,
        IsSystem: true
    );

    /// <summary>All system tool definitions in catalog order.</summary>
    public static IReadOnlyList<ToolDefinition> All { get; } =
    [
        WebSearch, KnowledgeBase, UrlFetch, NewsSearch,
        AcademicSearch, RestApi, StaticContext, LearningRetrieval
    ];
}
