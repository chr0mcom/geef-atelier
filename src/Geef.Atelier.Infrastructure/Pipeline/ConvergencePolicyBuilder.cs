using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Sdk.Policies;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class ConvergencePolicyBuilder
{
    public static DefaultConvergencePolicy Build(ConvergenceOptions defaults, ConvergencePolicyOverride? overridePolicy)
    {
        var maxIterations = overridePolicy?.MaxIterations ?? defaults.MaxIterations;
        var explicitMaxElapsedMinutes = overridePolicy?.MaxElapsedMinutes ?? defaults.MaxElapsedMinutes;

        return new()
        {
            MaxIterations          = maxIterations,
            // When no explicit time budget is set the SDK default (30 min) acts as a floor;
            // MinutesPerIteration below auto-scales it to maxIterations * minutes/iteration.
            MaxElapsedTime         = explicitMaxElapsedMinutes.HasValue
                ? TimeSpan.FromMinutes(explicitMaxElapsedMinutes.Value)
                : TimeSpan.FromMinutes(30),
            MinutesPerIteration    = defaults.MinutesPerIterationBudget,
            AbortOnCritical        = overridePolicy?.AbortOnCritical     ?? defaults.AbortOnCritical,
            DetectRegression       = overridePolicy?.DetectRegression    ?? defaults.DetectRegression,
            StagnationThreshold    = overridePolicy?.StagnationThreshold ?? defaults.StagnationThreshold,
            // Error/Critical findings block convergence; Info/Warning are non-blocking.
            BlockingSeverity       = FindingSeverity.Error,
            // Reviewer infrastructure failures are isolated by the SDK's InstrumentedReviewer.
            // Treat them as non-blocking so brief provider outages do not prevent convergence;
            // the failure is still recorded and visible in the run result.
            FailedReviewerHandling = FailedReviewerHandling.TreatAsNonBlocking
        };
    }
}
