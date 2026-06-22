using Geef.Atelier.Application.Pricing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Pricing;

internal sealed class PricingCatalog(
    IOptions<PricingOptions> options,
    ILogger<PricingCatalog> logger) : IPricingCatalog
{
    private readonly PricingOptions _options = options.Value;

    public decimal? CalculateCostEur(
        string modelName, int inputTokens, int outputTokens,
        string? providerName = null, int cachedInputTokens = 0)
    {
        if (!_options.Models.TryGetValue(modelName, out var pricing))
        {
            if (providerName is { Length: > 0 } &&
                _options.ProviderDefaults.TryGetValue(providerName, out var providerPricing))
            {
                pricing = providerPricing;
                logger.LogDebug(
                    "No model-specific pricing for {Model}; using provider-level default for {Provider}.",
                    modelName, providerName);
            }
            else
            {
                logger.LogWarning("No pricing entry for model {Model} or provider {Provider}. Cost will be null.",
                    modelName, providerName ?? "(none)");
                return null;
            }
        }

        // Cached input tokens are billed at a reduced rate by most providers. Split the input
        // into fresh + cached and apply the discount factor to the cached portion.
        var cached = Math.Clamp(cachedInputTokens, 0, inputTokens);
        var freshInput = inputTokens - cached;
        var inputCostUsd =
            ((decimal)freshInput / 1_000_000m * pricing.InputCostPerMillionTokensUsd)
            + ((decimal)cached / 1_000_000m * pricing.InputCostPerMillionTokensUsd
               * (decimal)_options.CachedInputDiscountFactor);
        var outputCostUsd = (decimal)outputTokens / 1_000_000m * pricing.OutputCostPerMillionTokensUsd;
        var totalUsd      = inputCostUsd + outputCostUsd;

        return totalUsd * (decimal)_options.UsdToEurRate;
    }
}
