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
}
