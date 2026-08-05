namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Hardcoded fallback model lists used when live provider API calls fail or for CLI providers
/// that have no HTTP model-listing endpoint.
/// Maintained by Atelier maintainers on each model-release cycle.
/// </summary>
public static class StaticModelFallback
{
    public static readonly IReadOnlyList<ModelInfo> ForOpenRouter = new ModelInfo[]
    {
        new("anthropic/claude-opus-5",      "Claude Opus 5",         "Best quality, Anthropic via OpenRouter",          true),
        new("anthropic/claude-sonnet-5",    "Claude Sonnet 5",       "Fast, balanced, Anthropic via OpenRouter",        true),
        new("anthropic/claude-haiku-4.5",   "Claude Haiku 4.5",      "Cheapest Anthropic model via OpenRouter",         true),
        new("google/gemini-3.6-flash",      "Gemini 3.6 Flash",      "Fast, cheap, Google via OpenRouter",              true),
        new("google/gemini-3.1-pro-preview","Gemini 3.1 Pro",        "High quality, Google via OpenRouter",             false),
        new("openai/gpt-5.6-sol",           "GPT-5.6 Sol",           "Flagship OpenAI model via OpenRouter",            true),
        new("openai/gpt-5.6-terra",         "GPT-5.6 Terra",         "Balanced OpenAI model via OpenRouter",            true),
        new("openai/gpt-5.6-luna",          "GPT-5.6 Luna",          "Cheap, fast, OpenAI via OpenRouter",              true),
        new("x-ai/grok-4.5",                "Grok 4.5",              "xAI flagship via OpenRouter",                     false),
        new("meta-llama/llama-4-maverick",  "Llama 4 Maverick",      "Open-weights, Meta via OpenRouter",               false),
    };

    public static readonly IReadOnlyList<ModelInfo> ForClaudeCli = new ModelInfo[]
    {
        new("claude-opus-latest",   "Claude Opus (latest)",   "Best quality, always the newest Opus",   true),
        new("claude-sonnet-latest", "Claude Sonnet (latest)", "Fast and balanced, always the newest",   true),
        new("claude-haiku-latest",  "Claude Haiku (latest)",  "Cheapest, always the newest Haiku",      true),
        new("claude-fable-latest",  "Claude Fable (latest)",  "Always the newest Fable",                true),
        new("claude-opus-5",        "Claude Opus 5",          "Pinned to this exact Opus generation",   false),
        new("claude-sonnet-5",      "Claude Sonnet 5",        "Pinned to this exact Sonnet generation", false),
        new("claude-haiku-4-5",     "Claude Haiku 4.5",       "Pinned to this exact Haiku generation",  false),
        new("claude-fable-5",       "Claude Fable 5",         "Pinned to this exact Fable generation",  false),
        new("claude-opus-4-8",      "Claude Opus 4.8",        "Previous Opus generation",               false),
    };

    /// <summary>
    /// Gemini has no live model source in the gateway, so this list is authoritative rather than a
    /// degradation — the picker labels it accordingly instead of claiming the provider is unreachable.
    /// </summary>
    public static readonly IReadOnlyList<ModelInfo> ForGeminiCli = new ModelInfo[]
    {
        new("gemini-3-1-pro",   "Gemini 3.1 Pro",   "Best quality, subscription or free tier", true),
        new("gemini-3-1-flash", "Gemini 3.1 Flash", "Fast and cheap",                          true),
        new("gemini-2-5-pro",   "Gemini 2.5 Pro",   "Previous generation, high quality",       false),
        new("gemini-2-5-flash", "Gemini 2.5 Flash", "Previous generation, fast",               false),
    };

    public static readonly IReadOnlyList<ModelInfo> ForCodexCli = new ModelInfo[]
    {
        new("gpt-5.6-sol",      "GPT-5.6 Sol",       "Flagship, latest generation",        true),
        new("gpt-5.6-terra",    "GPT-5.6 Terra",     "Balanced mid-tier",                  true),
        new("gpt-5.6-luna",     "GPT-5.6 Luna",      "Fastest and cheapest",               true),
        new("gpt-5.5",          "GPT-5.5",           "Previous generation flagship",       false),
        new("gpt-5.4",          "GPT-5.4",           "Legacy flagship",                    false),
        new("gpt-5.4-mini",     "GPT-5.4 Mini",      "Legacy, balanced",                   false),
    };

    /// <summary>Returns the fallback list for <paramref name="providerName"/>, or an empty list if unknown.</summary>
    public static IReadOnlyList<ModelInfo> For(string providerName) =>
        providerName.ToLowerInvariant() switch
        {
            "openrouter"  => ForOpenRouter,
            "claude-cli"  => ForClaudeCli,
            "codex-cli"   => ForCodexCli,
            "gemini-cli"  => ForGeminiCli,
            _             => Array.Empty<ModelInfo>(),
        };
}
