namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Configuration for the Tavily web-search grounding provider.
/// Bound from the <c>Tavily</c> section of appsettings.json; <c>ApiKey</c> from the
/// <c>TAVILY_API_KEY</c> environment variable (via <c>.env</c>).
/// </summary>
public sealed class TavilyOptions
{
    public string Endpoint { get; set; } = "https://api.tavily.com";
    public double BasicSearchCostUsd { get; set; } = 0.001;
    public double AdvancedSearchCostUsd { get; set; } = 0.002;
    public double UsdToEurRate { get; set; } = 0.92;
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Bound from <c>TAVILY_API_KEY</c> env var. When empty the provider is registered
    /// but throws <see cref="InvalidOperationException"/> on use — the app does not crash at startup.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;
}
