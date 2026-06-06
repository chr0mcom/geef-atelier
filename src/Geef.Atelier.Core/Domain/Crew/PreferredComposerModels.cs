namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Curated shortlist of top-tier models for auto-crew composition.
/// Only the newest, highest-quality models per provider are listed here.
/// The executor's model catalog block filters this list against the live provider catalog
/// at runtime, so stale entries are automatically excluded when a model is retired.
/// <para>
/// Maintenance: update this list on every significant model release.
/// Reference benchmarks: https://artificialanalysis.ai/models#intelligence-tabs
/// </para>
/// </summary>
public static class PreferredComposerModels
{
    /// <summary>
    /// Preferred executor model. Claude Opus via subscription CLI — best quality, zero token cost.
    /// </summary>
    public static readonly (string Provider, string Model) Executor =
        ("claude-cli", "claude-opus-4-8");

    /// <summary>
    /// Preferred reviewer models for model plurality.
    /// Must differ from <see cref="Executor"/> model to ensure independent perspectives.
    /// Ordered: best first.
    /// </summary>
    public static readonly (string Provider, string Model)[] Reviewers =
    [
        ("codex-cli",    "gpt-5.5"),                             // OpenAI top via subscription CLI
        ("openrouter",   "x-ai/grok-4.3"),                      // xAI top via OpenRouter
        ("openrouter",   "google/gemini-3.1-pro-preview"),       // Google top via OpenRouter
        ("openrouter",   "deepseek/deepseek-v4-pro"),            // DeepSeek top via OpenRouter
        ("openrouter",   "anthropic/claude-opus-4.8"),           // Anthropic top via OpenRouter (fallback)
    ];

    /// <summary>
    /// All preferred models flattened as (provider, model) pairs, for filtering against the live catalog.
    /// </summary>
    public static IEnumerable<(string Provider, string Model)> All =>
        Reviewers.Prepend(Executor);
}
