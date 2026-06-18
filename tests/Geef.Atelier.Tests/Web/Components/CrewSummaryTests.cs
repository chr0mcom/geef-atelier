using Bunit;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class CrewSummaryTests : TestContext
{
    private static ExecutorProfile MakeExecutor() => new(
        Name: "test-executor",
        DisplayName: "Test Executor",
        Description: "Desc",
        SystemPrompt: "Prompt",
        Provider: "openrouter",
        Model: "anthropic/claude-opus-4.8",
        MaxTokens: null,
        IsSystem: false);

    private static ReviewerProfile MakeReviewer(string name, string displayName) => new(
        Name: name,
        DisplayName: displayName,
        Description: "Desc",
        SystemPrompt: "Prompt",
        Provider: "openrouter",
        Model: "google/gemini-2.5-flash",
        MaxTokens: null,
        IsSystem: false);

    private static CrewSnapshot MakeSnapshot(string? templateName = "klassik",
        ConvergencePolicyOverride? convergenceOverride = null,
        IReadOnlyList<ReviewerProfile>? reviewers = null) =>
        new(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: templateName,
            Executor: MakeExecutor(),
            Reviewers: reviewers ?? new[] { MakeReviewer("r1", "Reviewer One"), MakeReviewer("r2", "Reviewer Two") },
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: convergenceOverride,
            Advisors: Array.Empty<AdvisorProfile>());

    [Fact]
    public void NullSnapshot_RendersFallbackLabel()
    {
        var cut = RenderComponent<CrewSummary>(p =>
        {
            p.Add(c => c.Snapshot, (CrewSnapshot?)null);
            p.Add(c => c.FallbackTemplateName, "MyFallback");
        });

        Assert.Contains("MyFallback", cut.Markup);
        Assert.Contains("system default", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullSnapshot_NullFallback_ShowsDefaultFallback()
    {
        var cut = RenderComponent<CrewSummary>(p =>
        {
            p.Add(c => c.Snapshot, (CrewSnapshot?)null);
            p.Add(c => c.FallbackTemplateName, (string?)null);
        });

        // Default fallback is "Classic"
        Assert.Contains("Classic", cut.Markup);
        Assert.Contains("system default", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WithSnapshot_ShowsCollapsedHeader_WithCrewCount()
    {
        var snapshot = MakeSnapshot();
        var cut = RenderComponent<CrewSummary>(p => p.Add(c => c.Snapshot, snapshot));

        // Should show executor + reviewer count in header
        Assert.Contains("1 Executor", cut.Markup);
        Assert.Contains("2 Reviewers", cut.Markup);

        // Body should not be visible when collapsed
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='crew-summary-body']"));
    }

    [Fact]
    public void ClickToggle_ExpandsBody_ShowsExecutorAndReviewerInfo()
    {
        var snapshot = MakeSnapshot();
        var cut = RenderComponent<CrewSummary>(p => p.Add(c => c.Snapshot, snapshot));

        cut.Find("[data-testid='crew-summary-toggle']").Click();

        var body = cut.Find("[data-testid='crew-summary-body']");
        Assert.Contains("Test Executor", body.TextContent);
        Assert.Contains("Reviewer One", body.TextContent);
        Assert.Contains("Reviewer Two", body.TextContent);
    }

    [Fact]
    public void ClickToggle_Twice_CollapsesBody()
    {
        var snapshot = MakeSnapshot();
        var cut = RenderComponent<CrewSummary>(p => p.Add(c => c.Snapshot, snapshot));

        cut.Find("[data-testid='crew-summary-toggle']").Click();
        cut.Find("[data-testid='crew-summary-toggle']").Click();

        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find("[data-testid='crew-summary-body']"));
    }

    [Fact]
    public void ConvergenceOverride_NonNull_ShowsOverrideSectionWhenExpanded()
    {
        var override_ = new ConvergencePolicyOverride(MaxIterations: 5, AbortOnCritical: true, DetectRegression: null, StagnationThreshold: null);
        var snapshot = MakeSnapshot(convergenceOverride: override_);
        var cut = RenderComponent<CrewSummary>(p => p.Add(c => c.Snapshot, snapshot));

        cut.Find("[data-testid='crew-summary-toggle']").Click();

        var body = cut.Find("[data-testid='crew-summary-body']");
        Assert.Contains("MaxIterations=5", body.TextContent);
        Assert.Contains("AbortOnCritical=True", body.TextContent);
    }

    [Fact]
    public void ConvergenceOverride_Null_NoOverrideSectionWhenExpanded()
    {
        var snapshot = MakeSnapshot(convergenceOverride: null);
        var cut = RenderComponent<CrewSummary>(p => p.Add(c => c.Snapshot, snapshot));

        cut.Find("[data-testid='crew-summary-toggle']").Click();

        var body = cut.Find("[data-testid='crew-summary-body']");
        Assert.DoesNotContain("Override", body.TextContent);
    }
}
