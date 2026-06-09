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
        // so a configured MaxIterations of e.g. 16 never takes effect.
        //
        // Policy: the iteration count governs. The budget auto-scales with MaxIterations (the floor),
        // and an explicit override/default can only ever RAISE it above that floor — never cut a run
        // off below its iteration budget. So an explicit 120 min with 16 iterations is lifted to the
        // 240 min the iterations need, while an explicit 300 min is honored as-is.
        var autoScaleFloor = Math.Max(30, maxIterations * Math.Max(1, defaults.MinutesPerIterationBudget));
        var configuredMinutes = overridePolicy?.MaxElapsedMinutes ?? defaults.MaxElapsedMinutes;
        var maxElapsedMinutes = configuredMinutes.HasValue
            ? Math.Max(configuredMinutes.Value, autoScaleFloor)
            : autoScaleFloor;

        return new()
        {
            MaxIterations          = maxIterations,
            MaxElapsedTime         = TimeSpan.FromMinutes(maxElapsedMinutes),
            AbortOnCritical        = overridePolicy?.AbortOnCritical     ?? defaults.AbortOnCritical,
            DetectRegression       = overridePolicy?.DetectRegression    ?? defaults.DetectRegression,
            StagnationThreshold    = overridePolicy?.StagnationThreshold ?? defaults.StagnationThreshold,
            // Reviewer infrastructure failures are isolated by the SDK's InstrumentedReviewer.
            // Treat them as non-blocking so brief provider outages do not prevent convergence;
            // the failure is still recorded and visible in the run result.
            FailedReviewerHandling = FailedReviewerHandling.TreatAsNonBlocking
        };
    }
}
