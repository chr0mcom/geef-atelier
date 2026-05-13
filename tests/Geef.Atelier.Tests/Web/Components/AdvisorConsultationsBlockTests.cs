using Bunit;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class AdvisorConsultationsBlockTests : TestContext
{
    private static AdvisorConsultation MakeConsultation(string advisorName, string output = "Some advisor output.") =>
        new(
            Id: Guid.NewGuid(),
            RunId: Guid.NewGuid(),
            IterationNumber: 1,
            AdvisorProfileName: advisorName,
            Output: output,
            CreatedAt: new DateTimeOffset(2026, 5, 13, 10, 30, 0, TimeSpan.Zero));

    [Fact]
    public void AdvisorConsultationsBlock_ShowsCollapsedState_ByDefault()
    {
        var consultations = new[] { MakeConsultation("briefing-clarifier") };
        var cut = RenderComponent<AdvisorConsultationsBlock>(p =>
        {
            p.Add(c => c.Consultations, consultations);
        });

        cut.Find("[data-testid='advisor-consultations']");
        // Body should not be visible when collapsed
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".collapsible-body"));
    }

    [Fact]
    public void AdvisorConsultationsBlock_ExpandsOnClick()
    {
        var consultations = new[] { MakeConsultation("briefing-clarifier") };
        var cut = RenderComponent<AdvisorConsultationsBlock>(p =>
        {
            p.Add(c => c.Consultations, consultations);
        });

        cut.Find(".collapsible-header").Click();

        cut.Find(".collapsible-body");
    }

    [Fact]
    public void AdvisorConsultationsBlock_ShowsAllConsultations_WhenExpanded()
    {
        var consultations = new[]
        {
            MakeConsultation("briefing-clarifier", "Briefing output."),
            MakeConsultation("devils-advocate", "Devil output."),
        };
        var cut = RenderComponent<AdvisorConsultationsBlock>(p =>
        {
            p.Add(c => c.Consultations, consultations);
        });

        cut.Find(".collapsible-header").Click();

        var body = cut.Find(".collapsible-body");
        Assert.Contains("briefing-clarifier", body.InnerHtml);
        Assert.Contains("devils-advocate", body.InnerHtml);
        Assert.Contains("Briefing output.", body.TextContent);
        Assert.Contains("Devil output.", body.TextContent);
    }

    [Fact]
    public void AdvisorConsultationsBlock_IsRecovery_ShowsDifferentHeading()
    {
        var consultations = new[] { MakeConsultation("briefing-clarifier") };
        var cut = RenderComponent<AdvisorConsultationsBlock>(p =>
        {
            p.Add(c => c.Consultations, consultations);
            p.Add(c => c.IsRecovery, true);
        });

        Assert.Contains("Recovery advisors", cut.Markup);
        Assert.DoesNotContain("Advisors consulted", cut.Markup);
    }

    [Fact]
    public void AdvisorConsultationsBlock_EmptyList_ShowsZeroCount()
    {
        var cut = RenderComponent<AdvisorConsultationsBlock>(p =>
        {
            p.Add(c => c.Consultations, Array.Empty<AdvisorConsultation>());
        });

        // Component renders but shows 0 advisors in the header
        cut.Find("[data-testid='advisor-consultations']");
        Assert.Contains("0 advisor", cut.Markup);
    }
}
