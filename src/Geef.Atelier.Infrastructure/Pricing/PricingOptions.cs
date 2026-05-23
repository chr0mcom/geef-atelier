using Geef.Atelier.Application.Pricing;

namespace Geef.Atelier.Infrastructure.Pricing;

public sealed class PricingOptions
{
    public double UsdToEurRate { get; set; } = 0.92;
    public Dictionary<string, ModelPricing> Models { get; set; } = new();
    /// <summary>
    /// Provider-level fallback pricing used when no model-specific entry is found.
    /// Keyed by provider name (e.g. "xai", "deepseek", "openai-direct").
    /// </summary>
    public Dictionary<string, ModelPricing> ProviderDefaults { get; set; } = new();
}

public sealed class CostTrackingOptions
{
    public bool Enabled { get; set; } = true;
}
