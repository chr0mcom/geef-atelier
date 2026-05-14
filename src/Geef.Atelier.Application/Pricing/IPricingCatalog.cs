namespace Geef.Atelier.Application.Pricing;

/// <summary>
/// Calculates LLM call costs in EUR from token counts and a configurable pricing table.
/// Returns null for unknown models rather than throwing.
/// </summary>
public interface IPricingCatalog
{
    /// <summary>
    /// Returns the cost in EUR for the given model and token counts,
    /// or null when the model has no entry in the pricing table.
    /// </summary>
    decimal? CalculateCostEur(string modelName, int inputTokens, int outputTokens);
}

public sealed record ModelPricing(
    decimal InputCostPerMillionTokensUsd,
    decimal OutputCostPerMillionTokensUsd);
