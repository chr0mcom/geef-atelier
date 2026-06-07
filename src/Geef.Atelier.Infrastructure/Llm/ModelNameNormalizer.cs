namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>
/// Normalises model-name strings so that profiles with different notation
/// (e.g. "anthropic/claude-opus-4.8" vs "claude-opus-4-8") resolve to the
/// same canonical form before being forwarded to the CLI proxy.
///
/// Rules (applied in order, only when the provider is a CLI provider):
///   1. Strip a leading "vendor/" prefix (anthropic/, openai/, google/).
///   2. Replace dots with dashes ("4.7" → "4-7").
///
/// HTTP-provider model names are returned unchanged — they must match whatever
/// the upstream API expects (e.g. Anthropic API uses "claude-opus-4-5-20241022").
/// </summary>
public static class ModelNameNormalizer
{
    // CLI provider names that require normalisation (prefix stripping).
    private static readonly HashSet<string> CliProviders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "claude-cli",
            "codex-cli",
            "gemini-cli",
        };

    // CLI providers that also replace dots with dashes (Claude + Gemini canonical form).
    // Codex/OpenAI models keep dots: "gpt-5.5" is the canonical GPT name.
    private static readonly HashSet<string> DotToHyphenProviders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "claude-cli",
            "gemini-cli",
        };

    // Known vendor prefixes to strip from CLI model names.
    private static readonly string[] VendorPrefixes =
        ["anthropic/", "openai/", "google/"];

    /// <summary>
    /// Returns true when the given provider routes through a local CLI agent
    /// (claude-cli, codex-cli, gemini-cli) rather than a direct HTTP API.
    /// Used by the executor to decide whether to enable document-mode editing.
    /// </summary>
    public static bool IsCliProvider(string providerName)
        => CliProviders.Contains(providerName);

    /// <summary>
    /// Returns the canonical model name for the given provider.
    /// Idempotent: already-normalised names are returned as-is.
    /// </summary>
    public static string Normalize(string providerName, string modelName)
    {
        if (!CliProviders.Contains(providerName))
            return modelName;

        var name = modelName.AsSpan().Trim();

        // Strip leading vendor prefix.
        foreach (var prefix in VendorPrefixes)
        {
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name[prefix.Length..];
                break;
            }
        }

        var result = name.ToString();

        // Replace dots with dashes only for providers where the canonical form uses dashes.
        return DotToHyphenProviders.Contains(providerName) && result.Contains('.')
            ? result.Replace('.', '-')
            : result;
    }
}
