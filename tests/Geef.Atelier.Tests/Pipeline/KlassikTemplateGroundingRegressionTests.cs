using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies that the Klassik system template ships with tavily-basic as its only grounding provider.
/// All system templates have internet access enabled by default.
/// </summary>
public sealed class KlassikTemplateGroundingRegressionTests
{
    [Fact]
    public void KlassikTemplate_HasTavilyBasicGroundingProvider()
    {
        Assert.Equal(2, SystemCrew.KlassikTemplate.GroundingProviderNames.Count);
        Assert.Contains("tavily-basic", SystemCrew.KlassikTemplate.GroundingProviderNames);
        Assert.Contains("run-attachments", SystemCrew.KlassikTemplate.GroundingProviderNames);
    }

    [Fact]
    public void KlassikTemplate_GroundingProviderNames_IsNotNull()
    {
        Assert.NotNull(SystemCrew.KlassikTemplate.GroundingProviderNames);
    }

    [Fact]
    public void KlassikSnapshot_ContainsTavilyBasic_InGroundingProviderNames()
    {
        // When CrewSnapshotBuilder resolves Klassik, GroundingProviders should contain tavily-basic.
        var template = SystemCrew.KlassikTemplate;
        Assert.Contains("tavily-basic", template.GroundingProviderNames);
    }
}
