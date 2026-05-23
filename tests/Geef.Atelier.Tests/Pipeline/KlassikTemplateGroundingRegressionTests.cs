using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies that the Klassik system template ships with tavily-basic, run-attachments and
/// learning-retriever-default as its default grounding providers. All system templates have
/// internet access and domain-aware learning retrieval enabled by default (D-054).
/// </summary>
public sealed class KlassikTemplateGroundingRegressionTests
{
    [Fact]
    public void KlassikTemplate_HasDefaultGroundingProviders()
    {
        Assert.Equal(3, SystemCrew.KlassikTemplate.GroundingProviderNames.Count);
        Assert.Contains("tavily-basic", SystemCrew.KlassikTemplate.GroundingProviderNames);
        Assert.Contains("run-attachments", SystemCrew.KlassikTemplate.GroundingProviderNames);
        Assert.Contains("learning-retriever-default", SystemCrew.KlassikTemplate.GroundingProviderNames);
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
