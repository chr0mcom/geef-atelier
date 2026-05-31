using Bunit;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class CrewSummaryWithAdvisorsTests : TestContext
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

    private static AdvisorProfile MakeAdvisor(string name, string displayName) => new(
        Name: name,
        DisplayName: displayName,
        Description: "Desc",
        SystemPrompt: "Prompt",
        Provider: "openrouter",
        Model: "google/gemini-2.5-flash-preview",
        MaxTokens: null,
        Mode: AdvisorMode.Strategic,
        Trigger: AdvisorTrigger.BeforeFirstExecution,
        IsSystem: false);

    private static CrewSnapshot MakeSnapshot(IReadOnlyList<AdvisorProfile>? advisors = null) =>
        new(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: "klassik",
            Executor: MakeExecutor(),
            Reviewers: new[] { MakeReviewer("r1", "Reviewer One") },
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: advisors ?? Array.Empty<AdvisorProfile>());

    [Fact]
    public void CrewSummary_WithAdvisors_ShowsAdvisorsSection_WhenExpanded()
    {
        var advisors = new[] { MakeAdvisor("briefing-clarifier", "Briefing Clarifier") };
        var snapshot = MakeSnapshot(advisors);
        var cut = RenderComponent<CrewSummary>(p => p.Add(c => c.Snapshot, snapshot));

        cut.Find("[data-testid='crew-summary-toggle']").Click();

        var body = cut.Find("[data-testid='crew-summary-body']");
        Assert.Contains("Advisors", body.TextContent);
        var advisorsSection = cut.Find("[data-testid='crew-summary-advisors']");
        Assert.Contains("Briefing Clarifier", advisorsSection.TextContent);
    }

    [Fact]
    public void CrewSummary_WithoutAdvisors_DoesNotShowAdvisorsSection()
    {
        var snapshot = MakeSnapshot(Array.Empty<AdvisorProfile>());
        var cut = RenderComponent<CrewSummary>(p => p.Add(c => c.Snapshot, snapshot));

        cut.Find("[data-testid='crew-summary-toggle']").Click();

        cut.Find("[data-testid='crew-summary-body']");
        Assert.Throws<Bunit.ElementNotFoundException>(() =>
            cut.Find("[data-testid='crew-summary-advisors']"));
    }
}
