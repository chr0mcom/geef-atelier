namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Hardcoded fallback model lists used when live provider API calls fail or for CLI providers
/// that have no HTTP model-listing endpoint.
/// Maintained by Atelier maintainers on each model-release cycle.
/// </summary>
public static class StaticModelFallback
{
    /// <summary>
    /// OpenRouter fallback. Claude and OpenAI models are deliberately absent: they always run through the
    /// subscription CLIs (D-066), so OpenRouter only serves other vendor families.
    /// </summary>
    public static readonly IReadOnlyList<ModelInfo> ForOpenRouter = new ModelInfo[]
    {
        new("google/gemini-4-neon",         "Gemini 4 Neon",         "Lowest hallucination rate, leads legal benchmarks (paid)", true),
        new("google/gemini-4-argon",        "Gemini 4 Argon",        "High quality Google model (paid)",                         false),
        new("google/gemini-4-hydrogen",     "Gemini 4 Hydrogen",     "Fast, cheaper Google model (paid)",                        false),
        new("moonshotai/kimi-k3.1-pro",     "Kimi K3.1 Pro",         "Long-context reasoning (paid)",                            false),
        new("mistralai/mistral-large-4-0",  "Mistral Large 4",       "European open-weights flagship (paid)",                    false),
        new("qwen/qwen4-max",               "Qwen 4 Max",            "Alibaba flagship (paid)",                                  false),
    };

    public static readonly IReadOnlyList<ModelInfo> ForClaudeCli = new ModelInfo[]
    {
        new("claude-opus-latest",   "Claude Opus (latest)",   "Best German prose, always the newest Opus",   true),
        new("claude-sonnet-latest", "Claude Sonnet (latest)", "Fast and faithful, always the newest Sonnet",   true),
        new("claude-haiku-latest",  "Claude Haiku (latest)",  "Cheapest, always the newest Haiku",      true),
        new("claude-fable-latest",  "Claude Fable (latest)",  "Strongest analysis, always the newest Fable",                true),
        new("claude-opus-5-5",      "Claude Opus 5.5",        "Pinned to this exact Opus generation",   false),
        new("claude-sonnet-5-5",    "Claude Sonnet 5.5",      "Pinned to this exact Sonnet generation", false),
        new("claude-haiku-5-5",     "Claude Haiku 5.5",       "Pinned to this exact Haiku generation",  false),
        new("claude-fable-5-2",     "Claude Fable 5.2",       "Pinned to this exact Fable generation",  false),
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
        new("gpt-sol-latest",   "GPT Sol (latest)",  "Always the newest Sol: fast, source-faithful, large context", true),
        new("gpt-astra-latest", "GPT Astra (latest)", "Always the newest Astra: most capable, facts and logic", true),
        new("gpt-terra-latest", "GPT Terra (latest)", "Always the newest Terra: efficient",                    true),
        new("gpt-6.2-sol",      "GPT-6.2 Sol",       "Go-to model: fast, source-faithful, 500k context", false),
        new("gpt-6.2-astra",    "GPT-6.2 Astra",     "Most capable: facts and logic",                    false),
        new("gpt-6.1-terra",    "GPT-6.1 Terra",     "Efficient previous generation",                    false),
        new("gpt-6.1-sol",      "GPT-6.1 Sol",       "Previous generation go-to model",                  false),
        new("gpt-6-astra",      "GPT-6 Astra",       "Older generation, most capable",                   false),
        new("gpt-6-sol",        "GPT-6 Sol",         "Older generation go-to model",                     false),
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
