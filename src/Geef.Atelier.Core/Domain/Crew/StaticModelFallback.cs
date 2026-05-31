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
        new("anthropic/claude-opus-4.8",    "Claude Opus 4.8",       "Best quality, Anthropic via OpenRouter",          true),
        new("anthropic/claude-sonnet-4.5",  "Claude Sonnet 4.5",     "Fast, balanced, Anthropic via OpenRouter",        true),
        new("anthropic/claude-haiku-4.5",   "Claude Haiku 4.5",      "Cheapest Anthropic model via OpenRouter",         true),
        new("google/gemini-2.5-flash",      "Gemini 2.5 Flash",      "Fast, cheap, Google via OpenRouter",              true),
        new("google/gemini-2.5-pro",        "Gemini 2.5 Pro",        "High quality, Google via OpenRouter",             false),
        new("openai/gpt-4o",                "GPT-4o",                "Flagship OpenAI model via OpenRouter",            true),
        new("openai/gpt-4o-mini",           "GPT-4o Mini",           "Cheap, fast, OpenAI via OpenRouter",              true),
        new("openai/o3",                    "o3",                    "Reasoning model, OpenAI via OpenRouter",          false),
        new("openai/o4-mini",               "o4-mini",               "Fast reasoning, OpenAI via OpenRouter",           false),
        new("meta-llama/llama-3.3-70b-instruct", "Llama 3.3 70B",   "Open-weights, Meta via OpenRouter",               false),
    };

    public static readonly IReadOnlyList<ModelInfo> ForClaudeCli = new ModelInfo[]
    {
        new("claude-opus-4-8",   "Claude Opus 4.8",   "Best quality, subscription required",  true),
        new("claude-sonnet-4-6", "Claude Sonnet 4.6", "Fast and balanced",                    true),
        new("claude-haiku-4-5",  "Claude Haiku 4.5",  "Cheapest, basic tasks",                true),
    };

    public static readonly IReadOnlyList<ModelInfo> ForCodexCli = new ModelInfo[]
    {
        new("gpt-5.5",          "GPT-5.5",           "Latest generation, 1M context",      true),
        new("gpt-5.4-nano",     "GPT-5.4 Nano",      "Fast and cheap, 400k context",       true),
        new("gpt-5.4-mini",     "GPT-5.4 Mini",      "Balanced, 400k context",             true),
        new("gpt-5.4-pro",      "GPT-5.4 Pro",       "High capability, 1M context",        false),
        new("gpt-5.4",          "GPT-5.4",           "Previous generation flagship",        false),
        new("gpt-5.3-codex",    "GPT-5.3 Codex",     "Coding-optimized model",             false),
        new("gpt-5.2-codex",    "GPT-5.2 Codex",     "Previous coding-optimized model",    false),
        new("o3-pro",           "o3 Pro",            "High-quality reasoning",              false),
        new("o4-mini",          "o4-mini",           "Fast reasoning",                      false),
    };

    /// <summary>Returns the fallback list for <paramref name="providerName"/>, or an empty list if unknown.</summary>
    public static IReadOnlyList<ModelInfo> For(string providerName) =>
        providerName.ToLowerInvariant() switch
        {
            "openrouter"  => ForOpenRouter,
            "claude-cli"  => ForClaudeCli,
            "codex-cli"   => ForCodexCli,
            _             => Array.Empty<ModelInfo>(),
        };
}
