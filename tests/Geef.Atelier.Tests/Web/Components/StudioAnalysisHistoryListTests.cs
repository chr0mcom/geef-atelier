using Bunit;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
using Geef.Atelier.Web.Components.UI;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class StudioAnalysisHistoryListTests : TestContext
{
    private static StudioAnalysisHistoryEntry BuildEntry(
        string taskDescription = "Test task",
        string? materializedTemplateName = null) => new(
        Guid.NewGuid(),
        taskDescription,
        "Reasoning summary",
        materializedTemplateName,
        0.02m,
        DateTimeOffset.UtcNow);

    private sealed class StubStudioService(
        IReadOnlyList<StudioAnalysisHistoryEntry> items,
        bool hasMore) : ITemplateStudioService
    {
        public Task<StudioAnalysesPage> ListRecentAnalysesAsync(int page, int pageSize, CancellationToken ct = default)
            => Task.FromResult(new StudioAnalysesPage(items, hasMore));

        public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, StudioModelChoice? overrideChoice, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, StudioModelChoice? overrideChoice, IProgress<string>? progress, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<StudioModelChoice> GetEffectiveDefaultAsync(CancellationToken ct = default)
            => Task.FromResult(new StudioModelChoice("openrouter", "test-model", 8192));

        public Task SaveDefaultAsync(StudioModelChoice choice, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<MaterializationResult> MaterializeAsync(Guid analysisId, MaterializationRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private void RegisterStudioService(IReadOnlyList<StudioAnalysisHistoryEntry>? items = null, bool hasMore = false)
    {
        Services.AddSingleton<ITemplateStudioService>(new StubStudioService(items ?? [], hasMore));
    }

    [Fact]
    public void StudioAnalysisHistoryList_EmptyState_ShowsEmptyMessage()
    {
        RegisterStudioService([]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        cut.Find("[data-testid='empty-state']");
    }

    [Fact]
    public void StudioAnalysisHistoryList_WithEntries_ShowsHistoryList()
    {
        RegisterStudioService([BuildEntry("Task A"), BuildEntry("Task B")]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        Assert.Equal(2, cut.FindAll("[data-testid='history-entry']").Count);
    }

    [Fact]
    public void StudioAnalysisHistoryList_WithMoreItems_ShowsShowMoreButton()
    {
        RegisterStudioService([BuildEntry()], hasMore: true);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        cut.Find("[data-testid='show-more-button']");
    }

    [Fact]
    public void StudioAnalysisHistoryList_WithoutMoreItems_NoShowMoreButton()
    {
        RegisterStudioService([BuildEntry()], hasMore: false);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        Assert.Empty(cut.FindAll("[data-testid='show-more-button']"));
    }

    [Fact]
    public void StudioAnalysisHistoryList_MaterializedEntry_ShowsMaterializedBadge()
    {
        RegisterStudioService([BuildEntry(materializedTemplateName: "my-template")]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        cut.Find("[data-testid='badge-materialized']");
    }

    [Fact]
    public void StudioAnalysisHistoryList_NonMaterializedEntry_NoBadgeMaterialized()
    {
        RegisterStudioService([BuildEntry(materializedTemplateName: null)]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        Assert.Empty(cut.FindAll("[data-testid='badge-materialized']"));
    }

    [Fact]
    public void StudioAnalysisHistoryList_TaskDescriptionVisible_InSummary()
    {
        RegisterStudioService([BuildEntry("Write a press release")]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        Assert.Contains("Write a press release", cut.Markup);
    }

    [Fact]
    public void StudioAnalysisHistoryList_ExpandEntry_ShowsDetails()
    {
        RegisterStudioService([BuildEntry("Task description")]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        // Initially no details visible
        Assert.Empty(cut.FindAll("[data-testid='entry-details']"));

        // Click to expand
        cut.Find("[data-testid='history-entry'] .entry-summary").Click();

        // Details now visible
        Assert.Single(cut.FindAll("[data-testid='entry-details']"));
    }

    [Fact]
    public void StudioAnalysisHistoryList_ExpandThenCollapse_HidesDetails()
    {
        RegisterStudioService([BuildEntry("Toggle task")]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        var summary = cut.Find("[data-testid='history-entry'] .entry-summary");

        // Expand
        summary.Click();
        Assert.Single(cut.FindAll("[data-testid='entry-details']"));

        // Collapse
        summary.Click();
        Assert.Empty(cut.FindAll("[data-testid='entry-details']"));
    }

    [Fact]
    public void StudioAnalysisHistoryList_ExpandedEntry_ShowsReAnalyzeButton()
    {
        RegisterStudioService([BuildEntry("Original task")]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        cut.Find("[data-testid='history-entry'] .entry-summary").Click();

        cut.Find("[data-testid='re-analyze-button']");
    }

    [Fact]
    public void StudioAnalysisHistoryList_ReAnalyzeButton_FiresCallback()
    {
        RegisterStudioService([BuildEntry("Original task")]);

        string? reAnalyzeTask = null;
        var cut = RenderComponent<StudioAnalysisHistoryList>(p =>
            p.Add(c => c.OnReAnalyze, (string s) => reAnalyzeTask = s));

        // Expand the entry
        cut.Find("[data-testid='history-entry'] .entry-summary").Click();

        // Click re-analyze button
        cut.Find("[data-testid='re-analyze-button']").Click();

        Assert.Equal("Original task", reAnalyzeTask);
    }

    [Fact]
    public void StudioAnalysisHistoryList_MaterializedEntry_ShowsViewTemplateLink()
    {
        RegisterStudioService([BuildEntry(materializedTemplateName: "my-template")]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        // Expand the entry to see the link
        cut.Find("[data-testid='history-entry'] .entry-summary").Click();

        cut.Find("[data-testid='view-template-link']");
    }

    [Fact]
    public void StudioAnalysisHistoryList_NonMaterializedEntry_NoViewTemplateLink()
    {
        RegisterStudioService([BuildEntry(materializedTemplateName: null)]);

        var cut = RenderComponent<StudioAnalysisHistoryList>();

        // Expand the entry
        cut.Find("[data-testid='history-entry'] .entry-summary").Click();

        Assert.Empty(cut.FindAll("[data-testid='view-template-link']"));
    }
}
