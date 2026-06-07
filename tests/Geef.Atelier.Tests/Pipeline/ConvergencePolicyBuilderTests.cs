using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Pipeline;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class ConvergencePolicyBuilderTests
{
    private static readonly ConvergenceOptions Defaults = new()
    {
        MaxIterations       = 5,
        AbortOnCritical     = true,
        DetectRegression    = true,
        StagnationThreshold = 2
    };

    [Fact]
    public void Build_WithNoOverride_UsesDefaults()
    {
        var policy = ConvergencePolicyBuilder.Build(Defaults, null);

        Assert.Equal(5, policy.MaxIterations);
        Assert.True(policy.AbortOnCritical);
        Assert.True(policy.DetectRegression);
        Assert.Equal(2, policy.StagnationThreshold);
    }

    [Fact]
    public void Build_WithPartialOverride_OverridesOnlySpecifiedFields()
    {
        var overridePolicy = new ConvergencePolicyOverride(MaxIterations: 10, AbortOnCritical: false, DetectRegression: null, StagnationThreshold: null);
        var policy         = ConvergencePolicyBuilder.Build(Defaults, overridePolicy);

        Assert.Equal(10, policy.MaxIterations);
        Assert.False(policy.AbortOnCritical);
        Assert.True(policy.DetectRegression);    // from defaults
        Assert.Equal(2, policy.StagnationThreshold); // from defaults
    }

    [Fact]
    public void Build_WithFullOverride_UsesAllOverrideValues()
    {
        var overridePolicy = new ConvergencePolicyOverride(
            MaxIterations:       3,
            AbortOnCritical:     false,
            DetectRegression:    false,
            StagnationThreshold: 1);
        var policy = ConvergencePolicyBuilder.Build(Defaults, overridePolicy);

        Assert.Equal(3, policy.MaxIterations);
        Assert.False(policy.AbortOnCritical);
        Assert.False(policy.DetectRegression);
        Assert.Equal(1, policy.StagnationThreshold);
    }

    // ── MaxElapsedTime (wall-clock budget) ──────────────────────────────────

    [Fact]
    public void Build_WithNullMaxElapsed_AutoScalesWithIterations()
    {
        // 16 iterations × 15 min/iteration = 240 min, so the iteration count is the binding limit
        // instead of the SDK's hidden 30-minute default.
        var defaults = new ConvergenceOptions { MaxElapsedMinutes = null, MinutesPerIterationBudget = 15 };
        var overridePolicy = new ConvergencePolicyOverride(
            MaxIterations: 16, AbortOnCritical: null, DetectRegression: null, StagnationThreshold: null);

        var policy = ConvergencePolicyBuilder.Build(defaults, overridePolicy);

        Assert.Equal(TimeSpan.FromMinutes(240), policy.MaxElapsedTime);
    }

    [Fact]
    public void Build_WithExplicitMaxElapsedAboveFloor_UsesIt()
    {
        // 4 iterations → floor = max(30, 4×15) = 60; explicit 120 is above the floor → honored.
        var defaults = new ConvergenceOptions { MaxIterations = 4, MinutesPerIterationBudget = 15, MaxElapsedMinutes = 120 };

        var policy = ConvergencePolicyBuilder.Build(defaults, null);

        Assert.Equal(TimeSpan.FromMinutes(120), policy.MaxElapsedTime);
    }

    [Fact]
    public void Build_WithExplicitMaxElapsedBelowIterationFloor_IsRaisedToFloor()
    {
        // The iteration count governs: an explicit 120 with 16 iterations (floor 240) is lifted to 240,
        // so a too-low time cap can never cut a run off below its iteration budget.
        var defaults = new ConvergenceOptions { MinutesPerIterationBudget = 15 };
        var overridePolicy = new ConvergencePolicyOverride(
            MaxIterations: 16, AbortOnCritical: null, DetectRegression: null,
            StagnationThreshold: null, MaxElapsedMinutes: 120);

        var policy = ConvergencePolicyBuilder.Build(defaults, overridePolicy);

        Assert.Equal(TimeSpan.FromMinutes(240), policy.MaxElapsedTime);
    }

    [Fact]
    public void Build_WithOverrideMaxElapsedAboveFloor_IsHonored()
    {
        // 4 iterations → floor 60; override 90 is above the floor → honored as-is.
        var defaults = new ConvergenceOptions { MaxElapsedMinutes = null, MinutesPerIterationBudget = 15 };
        var overridePolicy = new ConvergencePolicyOverride(
            MaxIterations: 4, AbortOnCritical: null, DetectRegression: null,
            StagnationThreshold: null, MaxElapsedMinutes: 90);

        var policy = ConvergencePolicyBuilder.Build(defaults, overridePolicy);

        Assert.Equal(TimeSpan.FromMinutes(90), policy.MaxElapsedTime);
    }
}
