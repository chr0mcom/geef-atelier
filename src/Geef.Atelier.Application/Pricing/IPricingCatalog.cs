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
    decimal? CalculateCostEur(string modelName, int inputTokens, int outputTokens, string? providerName = null);
}

public sealed record ModelPricing(
    decimal InputCostPerMillionTokensUsd,
    decimal OutputCostPerMillionTokensUsd);
