namespace Geef.Atelier.Infrastructure.Llm;

internal static class HttpClientNames
{
    internal const string Llm = "llm";

    /// <summary>
    /// Short-timeout client for cli-proxy model lookups, deliberately without a resilience handler:
    /// retries and the shared circuit breaker of the "llm" client belong to real completions, and a
    /// failing model lookup must never open that breaker for them.
    /// </summary>
    internal const string CliProxyModels = "cli-proxy-models";
}
