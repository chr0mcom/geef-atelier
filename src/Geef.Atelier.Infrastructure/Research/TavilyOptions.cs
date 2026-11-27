namespace Geef.Atelier.Infrastructure.Research;

/// <summary>
/// Connection to the Tavily web search used when looking things up for an order (section <c>Tavily</c>); <c>ApiKey</c> comes from the
/// <c>TAVILY_API_KEY</c> environment variable. Without a key nothing is sent and the research ends as <c>no_key</c>.
/// </summary>
public sealed class TavilyOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Tavily";

    /// <summary>Base address of the search service.</summary>
    public string Endpoint { get; set; } = "https://api.tavily.com";

    /// <summary>The key of the search service; empty when none is configured.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
