using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Infrastructure.Pricing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Infrastructure.Pricing;

public sealed class PricingCatalogTests
{
    private static IPricingCatalog BuildCatalog(
        Dictionary<string, ModelPricing>? models = null,
        double usdToEurRate = 0.92)
    {
        var opts = new PricingOptions
        {
            UsdToEurRate = usdToEurRate,
            Models = models ?? new Dictionary<string, ModelPricing>
            {
                ["anthropic/claude-sonnet-4-5"] = new ModelPricing(3m, 15m)
            }
        };
        return new PricingCatalog(Options.Create(opts), NullLogger<PricingCatalog>.Instance);
    }

    [Fact]
    public void CalculateCostEur_KnownModel_ReturnsCorrectCost()
    {
        // 1M input tokens = $3.00, 1M output tokens = $15.00
        // 100k input + 20k output → $0.30 + $0.30 = $0.60 USD → $0.552 EUR
        var catalog = BuildCatalog();

        var cost = catalog.CalculateCostEur("anthropic/claude-sonnet-4-5", 100_000, 20_000);

        Assert.NotNull(cost);
        Assert.Equal(0.552m, cost!.Value, precision: 6);
    }

    [Fact]
    public void CalculateCostEur_ZeroTokens_ReturnsZero()
    {
        var catalog = BuildCatalog();

        var cost = catalog.CalculateCostEur("anthropic/claude-sonnet-4-5", 0, 0);

        Assert.NotNull(cost);
        Assert.Equal(0m, cost!.Value);
    }

    [Fact]
    public void CalculateCostEur_UnknownModel_ReturnsNull()
    {
        var catalog = BuildCatalog();

        var cost = catalog.CalculateCostEur("unknown/model-xyz", 1000, 500);

        Assert.Null(cost);
    }

    [Fact]
    public void CalculateCostEur_AppliesUsdToEurRate()
    {
        // 100k tokens input at $10/M = $1.00 USD, rate 0.5 → €0.50
        var catalog = BuildCatalog(
            models: new Dictionary<string, ModelPricing> { ["test/model"] = new ModelPricing(10m, 0m) },
            usdToEurRate: 0.5);

        var cost = catalog.CalculateCostEur("test/model", 100_000, 0);

        Assert.NotNull(cost);
        Assert.Equal(0.5m, cost!.Value, precision: 6);
    }

    [Fact]
    public void CalculateCostEur_InputAndOutputSeparateRates_CombinesCorrectly()
    {
        // $3 input / $15 output per million, rate = 1.0 for simple numbers
        var catalog = BuildCatalog(
            models: new Dictionary<string, ModelPricing> { ["m"] = new ModelPricing(3m, 15m) },
            usdToEurRate: 1.0);

        // 500k input + 500k output → $1.50 + $7.50 = $9.00
        var cost = catalog.CalculateCostEur("m", 500_000, 500_000);

        Assert.NotNull(cost);
        Assert.Equal(9.0m, cost!.Value, precision: 4);
    }
}
