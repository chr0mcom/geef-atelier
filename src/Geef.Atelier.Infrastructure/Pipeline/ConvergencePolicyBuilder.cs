using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Sdk.Policies;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class ConvergencePolicyBuilder
{
    public static DefaultConvergencePolicy Build(ConvergenceOptions defaults, ConvergencePolicyOverride? overridePolicy)
    {
        var maxIterations = overridePolicy?.MaxIterations ?? defaults.MaxIterations;

        // The SDK's DefaultConvergencePolicy enforces a wall-clock limit (MaxElapsedTime, SDK default
        // 30 min) that — when exceeded — stops the run with the SAME StopMaxAttemptsReached reason as
        // the iteration cap. If left at the default, heavy crews hit 30 min after only 3–4 iterations,
        // so a configured MaxIterations of e.g. 16 never takes effect. We therefore set the budget
        // explicitly: an explicit override/default wins, otherwise it auto-scales with MaxIterations so
        // the iteration count is the binding constraint.
        var maxElapsedMinutes =
            overridePolicy?.MaxElapsedMinutes
            ?? defaults.MaxElapsedMinutes
            ?? Math.Max(30, maxIterations * Math.Max(1, defaults.MinutesPerIterationBudget));

        return new()
        {
            MaxIterations       = maxIterations,
            MaxElapsedTime      = TimeSpan.FromMinutes(maxElapsedMinutes),
            AbortOnCritical     = overridePolicy?.AbortOnCritical     ?? defaults.AbortOnCritical,
            DetectRegression    = overridePolicy?.DetectRegression    ?? defaults.DetectRegression,
            StagnationThreshold = overridePolicy?.StagnationThreshold ?? defaults.StagnationThreshold
        };
    }
}
