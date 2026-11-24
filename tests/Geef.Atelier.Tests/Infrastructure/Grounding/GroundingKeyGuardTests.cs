using Geef.Atelier.Infrastructure.Grounding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>The startup guard names missing grounding keys and never needs their values.</summary>
public sealed class GroundingKeyGuardTests
{
    [Fact]
    public void ReportsMissingKeysByName()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Tavily:ApiKey"] = "" })
            .Build();

        var missing = configuration.FindMissingKeys().Select(k => k.Key).ToList();

        Assert.Equal(["Tavily:ApiKey", "Llm:Providers:openrouter:ApiKey"], missing);
    }

    [Fact]
    public void ContainerStyleEnvironmentVariablesSatisfyTheGuard()
    {
        Environment.SetEnvironmentVariable("GKG_TEST_Tavily__ApiKey", "x");
        Environment.SetEnvironmentVariable("GKG_TEST_Llm__Providers__openrouter__ApiKey", "y");
        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables("GKG_TEST_").Build();

            Assert.Empty(configuration.FindMissingKeys());
        }
        finally
        {
            Environment.SetEnvironmentVariable("GKG_TEST_Tavily__ApiKey", null);
            Environment.SetEnvironmentVariable("GKG_TEST_Llm__Providers__openrouter__ApiKey", null);
        }
    }

    [Fact]
    public void LegacyTavilyProviderReadsTheKeyNameTheContainerSets()
    {
        Environment.SetEnvironmentVariable("GKG_TAVILY_Tavily__ApiKey", "container-key");
        try
        {
            var configuration = new ConfigurationBuilder().AddEnvironmentVariables("GKG_TAVILY_").Build();
            var services = new ServiceCollection().AddGroundingProviders(configuration).BuildServiceProvider();

            Assert.Equal("container-key", services.GetRequiredService<IOptions<TavilyOptions>>().Value.ApiKey);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GKG_TAVILY_Tavily__ApiKey", null);
        }
    }
}
