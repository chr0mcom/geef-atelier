using Geef.Atelier.Application.Pricing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Pricing;

internal sealed class PricingCatalog(
    IOptions<PricingOptions> options,
    ILogger<PricingCatalog> logger) : IPricingCatalog
{
    private readonly PricingOptions _options = options.Value;

    public decimal? CalculateCostEur(string modelName, int inputTokens, int outputTokens)
    {
        if (!_options.Models.TryGetValue(modelName, out var pricing))
        {
            logger.LogWarning("No pricing entry for model {Model}. Cost will be null.", modelName);
            return null;
        }

        var inputCostUsd  = (decimal)inputTokens  / 1_000_000m * pricing.InputCostPerMillionTokensUsd;
        var outputCostUsd = (decimal)outputTokens / 1_000_000m * pricing.OutputCostPerMillionTokensUsd;
        var totalUsd      = inputCostUsd + outputCostUsd;

        return totalUsd * (decimal)_options.UsdToEurRate;
    }
}
