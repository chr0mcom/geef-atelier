using Geef.Atelier.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline;

public sealed class ConvergencePolicyConfigTests
{
    [Fact]
    public void ConvergenceOptions_DefaultValues_AreCorrect()
    {
        var opts = new ConvergenceOptions();
        Assert.Equal(3, opts.MaxIterations);
        Assert.False(opts.AbortOnCritical);
        Assert.True(opts.DetectRegression);
        Assert.Equal(3, opts.StagnationThreshold);
    }

    [Fact]
    public void ConvergenceOptions_LoadsFromConfiguration()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Convergence:MaxIterations"]       = "5",
                ["Convergence:AbortOnCritical"]     = "true",
                ["Convergence:DetectRegression"]    = "false",
                ["Convergence:StagnationThreshold"] = "2"
            })
            .Build();

        var services = new ServiceCollection();
        services.Configure<ConvergenceOptions>(config.GetSection("Convergence"));
        var provider = services.BuildServiceProvider();
        var opts     = provider.GetRequiredService<IOptions<ConvergenceOptions>>().Value;

        Assert.Equal(5, opts.MaxIterations);
        Assert.True(opts.AbortOnCritical);
        Assert.False(opts.DetectRegression);
        Assert.Equal(2, opts.StagnationThreshold);
    }

    [Fact]
    public void ConvergenceOptions_MissingSection_UsesDefaults()
    {
        var config = new ConfigurationBuilder().Build(); // empty config

        var services = new ServiceCollection();
        services.Configure<ConvergenceOptions>(config.GetSection("Convergence"));
        var provider = services.BuildServiceProvider();
        var opts     = provider.GetRequiredService<IOptions<ConvergenceOptions>>().Value;

        Assert.Equal(3, opts.MaxIterations);
        Assert.False(opts.AbortOnCritical);
    }
}
