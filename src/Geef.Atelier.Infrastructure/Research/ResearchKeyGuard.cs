using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>Startup check for the key of the web search. Reports only the key name, never its value.</summary>
public static class ResearchKeyGuard
{
    /// <summary>The configuration key of the web search.</summary>
    public const string TavilyKey = "Tavily:ApiKey";

    /// <summary>Logs a warning when <see cref="TavilyKey"/> is missing or empty; returns whether it is missing.</summary>
    public static bool WarnAboutMissingResearchKey(this IConfiguration configuration, ILogger logger)
    {
        if (!string.IsNullOrWhiteSpace(configuration[TavilyKey]))
            return false;
        logger.LogWarning("Configuration key {Key} is not set; looking things up on the web is skipped for every order.", TavilyKey);
        return true;
    }
}
