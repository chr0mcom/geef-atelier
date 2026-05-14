using Bunit;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class StudioReviewStepTests : TestContext
{
    private static TemplateStudioAnalysis MakeAnalysis(
        string reasoningSummary = "Test summary.",
        IReadOnlyList<TemplateMatch>? matches = null,
        ProposedTemplate? proposedTemplate = null,
        IReadOnlyList<ProposedProfile>? newProfiles = null) => new(
        Id: Guid.NewGuid(),
        TaskDescription: "A test task description.",
        MatchedExistingTemplates: matches ?? [],
        Recommendation: proposedTemplate is null
            ? StudioRecommendation.UseExistingTemplate
            : StudioRecommendation.CreateNewTemplate,
        ProposedTemplate: proposedTemplate,
        ProposedNewProfiles: newProfiles ?? [],
        ReasoningSummary: reasoningSummary,
        InputTokens: 100,
        OutputTokens: 50,
        CostEur: null,
        CreatedAt: DateTimeOffset.UtcNow);

    private static ProposedTemplate MakeTemplate() => new(
        Name: "new-template",
        DisplayName: "New Template",
        Description: "A proposed template.",
        ExecutorProfileName: "default-executor",
        ReviewerProfileNames: ["briefing-fidelity"],
        AdvisorProfileNames: [],
        GroundingProviderProfileNames: [],
        EvaluationStrategy: "Sequential");

    [Fact]
    public void StudioReviewStep_RendersReasoningSummary()
    {
        var analysis = MakeAnalysis(reasoningSummary: "Use the klassik template for this task.");

        var cut = RenderComponent<StudioReviewStep>(p =>
        {
            p.Add(c => c.Analysis, analysis);
        });

        var summary = cut.Find("[data-testid='reasoning-summary']");
        Assert.Contains("Use the klassik template for this task.", summary.TextContent);
    }

    [Fact]
    public void StudioReviewStep_RendersMatchedTemplatesWithConfidence()
    {
        var matches = new List<TemplateMatch>
        {
            new("klassik", 0.9, "Great fit for standard text."),
            new("legal-review", 0.6, "Partial fit.")
        };
        var analysis = MakeAnalysis(matches: matches);

        var cut = RenderComponent<StudioReviewStep>(p =>
        {
            p.Add(c => c.Analysis, analysis);
        });

        cut.Find("[data-testid='matched-templates']");
        cut.Find("[data-testid='template-match-klassik']");
        cut.Find("[data-testid='template-match-legal-review']");
    }

    [Fact]
    public void StudioReviewStep_ShowsUseTemplateButton_WhenConfidenceAbove85()
    {
        var matches = new List<TemplateMatch>
        {
            new("klassik", 0.95, "Excellent match."),
            new("other", 0.70, "Below threshold.")
        };
        var analysis = MakeAnalysis(matches: matches);

        var cut = RenderComponent<StudioReviewStep>(p =>
        {
            p.Add(c => c.Analysis, analysis);
        });

        // Only "klassik" has confidence >= 0.85
        cut.Find("[data-testid='use-template-klassik']");
        Assert.Throws<Bunit.ElementNotFoundException>(() =>
            cut.Find("[data-testid='use-template-other']"));
    }

    [Fact]
    public void StudioReviewStep_ShowsProceedToEditButton_WhenProposedTemplateExists()
    {
        var analysis = MakeAnalysis(proposedTemplate: MakeTemplate());

        var cut = RenderComponent<StudioReviewStep>(p =>
        {
            p.Add(c => c.Analysis, analysis);
        });

        cut.Find("[data-testid='proceed-to-edit-button']");
        cut.Find("[data-testid='proposed-template']");
    }
}
