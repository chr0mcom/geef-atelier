namespace Geef.Atelier.Application.Pricing;

/// <summary>
/// Calculates LLM call costs in EUR from token counts and a configurable pricing table.
/// Returns null for unknown models rather than throwing.
/// </summary>
public interface IPricingCatalog
{
    /// <summary>
    /// Returns the cost in EUR for the given model and token counts,
    /// or null when neither the model nor the provider has an entry in the pricing table.
    /// When <paramref name="providerName"/> is supplied and the model is not found, the catalog
    /// falls back to the provider-level default pricing (if configured).
    /// </summary>
    /// <param name="modelName">The model identifier used to look up pricing.</param>
    /// <param name="inputTokens">Total prompt tokens (including any cached subset).</param>
    /// <param name="outputTokens">Total completion tokens.</param>
    /// <param name="providerName">Optional provider name for provider-level fallback pricing.</param>
    /// <param name="cachedInputTokens">
    /// Subset of <paramref name="inputTokens"/> served from prompt cache; billed at a reduced rate
    /// (see PricingOptions.CachedInputDiscountFactor). 0 = no cached tokens / not reported.
    /// </param>
    decimal? CalculateCostEur(
        string modelName, int inputTokens, int outputTokens,
        string? providerName = null, int cachedInputTokens = 0);
}

public sealed record ModelPricing(
    decimal InputCostPerMillionTokensUsd,
    decimal OutputCostPerMillionTokensUsd);
