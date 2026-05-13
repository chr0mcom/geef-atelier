using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies that the Klassik system template ships with no grounding providers, preserving the
/// pre-grounding baseline. A Klassik run must never trigger Tavily or any other grounding provider.
/// </summary>
public sealed class KlassikTemplateGroundingRegressionTests
{
    [Fact]
    public void KlassikTemplate_HasNoGroundingProviders()
    {
        Assert.Empty(SystemCrew.KlassikTemplate.GroundingProviderNames);
    }

    [Fact]
    public void KlassikTemplate_GroundingProviderNames_IsNotNull()
    {
        Assert.NotNull(SystemCrew.KlassikTemplate.GroundingProviderNames);
    }

    [Fact]
    public void KlassikSnapshot_WouldProduceNoGroundingProviders_InSnapshot()
    {
        // When CrewSnapshotBuilder resolves Klassik, GroundingProviders should always be empty
        // because GroundingProviderNames is []. This test verifies the template-level constraint
        // without needing a full DB round-trip.
        var template = SystemCrew.KlassikTemplate;
        Assert.Empty(template.GroundingProviderNames);
    }
}
