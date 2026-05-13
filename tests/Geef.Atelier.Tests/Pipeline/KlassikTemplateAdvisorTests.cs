using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Pipeline;

/// <summary>
/// Verifies that the Klassik system template ships with no advisors, preserving the pre-PS-7 baseline.
/// </summary>
public sealed class KlassikTemplateAdvisorTests
{
    [Fact]
    public void KlassikTemplate_HasNoAdvisors_InCrewSnapshot()
    {
        Assert.Empty(SystemCrew.KlassikTemplate.AdvisorProfileNames);
    }

    [Fact]
    public void KlassikTemplate_AdvisorProfileNames_IsEmptyCollection()
    {
        // The field should be an empty collection, not null
        Assert.NotNull(SystemCrew.KlassikTemplate.AdvisorProfileNames);
        Assert.Empty(SystemCrew.KlassikTemplate.AdvisorProfileNames);
    }
}
