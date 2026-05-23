using Geef.Atelier.Application.Pricing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Pricing;

internal sealed class PricingCatalog(
    IOptions<PricingOptions> options,
    ILogger<PricingCatalog> logger) : IPricingCatalog
{
    private readonly PricingOptions _options = options.Value;

    public decimal? CalculateCostEur(string modelName, int inputTokens, int outputTokens, string? providerName = null)
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

        var inputCostUsd  = (decimal)inputTokens  / 1_000_000m * pricing.InputCostPerMillionTokensUsd;
        var outputCostUsd = (decimal)outputTokens / 1_000_000m * pricing.OutputCostPerMillionTokensUsd;
        var totalUsd      = inputCostUsd + outputCostUsd;

        return totalUsd * (decimal)_options.UsdToEurRate;
    }
}
