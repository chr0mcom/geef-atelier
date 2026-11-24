using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Startup check for the configuration keys the system grounding profiles depend on. Reports only key names and
/// the profiles affected, never values.
/// </summary>
public static class GroundingKeyGuard
{
    /// <summary>Configuration keys used by system grounding profiles, with the profiles that need them.</summary>
    public static readonly IReadOnlyList<(string Key, string Profiles)> RequiredKeys =
    [
        ("Tavily:ApiKey", "tavily-refined, tavily-basic, tavily-news"),
        ("Llm:Providers:openrouter:ApiKey", "run-attachments, knowledge-base-default (embeddings)"),
    ];

    /// <summary>Returns the required keys that are missing or empty in <paramref name="configuration"/>.</summary>
    public static IReadOnlyList<(string Key, string Profiles)> FindMissingKeys(this IConfiguration configuration) =>
        RequiredKeys.Where(k => string.IsNullOrWhiteSpace(configuration[k.Key])).ToList();

    /// <summary>Logs one warning per missing key; returns the number of missing keys.</summary>
    public static int WarnAboutMissingGroundingKeys(this IConfiguration configuration, ILogger logger)
    {
        var missing = configuration.FindMissingKeys();
        foreach (var (key, profiles) in missing)
            logger.LogWarning(
                "Configuration key {Key} is not set; the grounding profiles {Profiles} will be skipped with a warning on every run that uses them.",
                key, profiles);
        return missing.Count;
    }
}
