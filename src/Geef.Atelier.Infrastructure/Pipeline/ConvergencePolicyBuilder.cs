using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Sdk.Policies;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class ConvergencePolicyBuilder
{
    public static DefaultConvergencePolicy Build(ConvergenceOptions defaults, ConvergencePolicyOverride? overridePolicy)
        => new()
        {
            MaxIterations       = overridePolicy?.MaxIterations       ?? defaults.MaxIterations,
            AbortOnCritical     = overridePolicy?.AbortOnCritical     ?? defaults.AbortOnCritical,
            DetectRegression    = overridePolicy?.DetectRegression    ?? defaults.DetectRegression,
            StagnationThreshold = overridePolicy?.StagnationThreshold ?? defaults.StagnationThreshold
        };
}
