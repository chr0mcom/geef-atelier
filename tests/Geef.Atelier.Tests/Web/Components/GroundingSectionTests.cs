using Bunit;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class GroundingSectionTests : TestContext
{
    private static AdvisorConsultation MakeConsultation(string profileName = "briefing-clarifier") =>
        new(
            Id:                 Guid.NewGuid(),
            RunId:              Guid.NewGuid(),
            IterationNumber:    1,
            AdvisorProfileName: profileName,
            Output:             "Advisor output.",
            CreatedAt:          new DateTimeOffset(2026, 5, 13, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void GroundingSection_AlwaysRendersGroundingSectionTestId()
    {
        var cut = RenderComponent<GroundingSection>(p =>
        {
            p.Add(c => c.Briefing,          "My briefing");
            p.Add(c => c.GroundedBrief,     "My grounded brief");
            p.Add(c => c.BeforeFirstAdvisors, Array.Empty<AdvisorConsultation>());
        });

        cut.Find("[data-testid='grounding-section']");
    }

    [Fact]
    public void GroundingSection_AlwaysRendersGroundingBriefTestId()
    {
        var cut = RenderComponent<GroundingSection>(p =>
        {
            p.Add(c => c.Briefing,          "My briefing");
            p.Add(c => c.GroundedBrief,     "My grounded brief");
            p.Add(c => c.BeforeFirstAdvisors, Array.Empty<AdvisorConsultation>());
        });

        cut.Find("[data-testid='grounding-brief']");
    }

    [Fact]
    public void EmptyBeforeFirstAdvisors_GroundingAdvisorsSection_NotRendered()
    {
        var cut = RenderComponent<GroundingSection>(p =>
        {
            p.Add(c => c.Briefing,          "My briefing");
            p.Add(c => c.GroundedBrief,     "My grounded brief");
            p.Add(c => c.BeforeFirstAdvisors, Array.Empty<AdvisorConsultation>());
        });

        Assert.Throws<Bunit.ElementNotFoundException>(
            () => cut.Find("[data-testid='grounding-advisors']"));
    }

    [Fact]
    public void OneBeforeFirstAdvisor_GroundingAdvisorsSection_RenderedWithCount()
    {
        var consultations = new[] { MakeConsultation("briefing-clarifier") };
        var cut = RenderComponent<GroundingSection>(p =>
        {
            p.Add(c => c.Briefing,          "My briefing");
            p.Add(c => c.GroundedBrief,     "My grounded brief");
            p.Add(c => c.BeforeFirstAdvisors, consultations);
        });

        var advisorsSection = cut.Find("[data-testid='grounding-advisors']");
        Assert.Contains("1", advisorsSection.TextContent);
    }

    [Fact]
    public void MultipleBeforeFirstAdvisors_GroundingAdvisorsSection_ShowsCorrectCount()
    {
        var consultations = new[]
        {
            MakeConsultation("briefing-clarifier"),
            MakeConsultation("devils-advocate"),
        };
        var cut = RenderComponent<GroundingSection>(p =>
        {
            p.Add(c => c.Briefing,          "My briefing");
            p.Add(c => c.GroundedBrief,     "My grounded brief");
            p.Add(c => c.BeforeFirstAdvisors, consultations);
        });

        var advisorsSection = cut.Find("[data-testid='grounding-advisors']");
        Assert.Contains("2", advisorsSection.TextContent);
    }

    [Fact]
    public void BriefingText_RenderedInMarkup()
    {
        const string briefing = "Unique briefing text ABC123";
        var cut = RenderComponent<GroundingSection>(p =>
        {
            p.Add(c => c.Briefing,          briefing);
            p.Add(c => c.GroundedBrief,     "Grounded version");
            p.Add(c => c.BeforeFirstAdvisors, Array.Empty<AdvisorConsultation>());
        });

        Assert.Contains(briefing, cut.Markup);
    }
}
