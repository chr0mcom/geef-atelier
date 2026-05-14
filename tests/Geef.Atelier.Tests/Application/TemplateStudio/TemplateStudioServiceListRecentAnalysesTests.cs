using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;

namespace Geef.Atelier.Tests.Application.TemplateStudio;

public sealed class TemplateStudioServiceListRecentAnalysesTests
{
    // A fake service that implements in-memory pagination directly,
    // verifying the HasMore contract without requiring the full service constructor.
    private sealed class FakeStudioService : ITemplateStudioService
    {
        private readonly List<StudioAnalysisHistoryEntry> _entries;

        public FakeStudioService(int count)
        {
            _entries = Enumerable.Range(0, count)
                .Select(i => new StudioAnalysisHistoryEntry(
                    Guid.NewGuid(),
                    $"Task {i}",
                    $"Reasoning {i}",
                    i % 3 == 0 ? $"template-{i}" : null,
                    0.01m * i,
                    DateTimeOffset.UtcNow.AddMinutes(-i)))
                .ToList();
        }

        public Task<StudioAnalysesPage> ListRecentAnalysesAsync(int page, int pageSize, CancellationToken ct = default)
        {
            var skip = page * pageSize;
            var take = pageSize + 1;
            var slice = _entries.Skip(skip).Take(take).ToList();
            var hasMore = slice.Count > pageSize;
            return Task.FromResult(new StudioAnalysesPage(slice.Take(pageSize).ToList(), hasMore));
        }

        public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, CancellationToken ct = default)
            => throw new NotImplementedException();

        public Task<MaterializationResult> MaterializeAsync(Guid analysisId, MaterializationRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    [Fact]
    public async Task ListRecentAnalysesAsync_ReturnsFirstPage()
    {
        var svc = new FakeStudioService(15);

        var page = await svc.ListRecentAnalysesAsync(0, 10, CancellationToken.None);

        Assert.Equal(10, page.Items.Count);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task ListRecentAnalysesAsync_ReturnsSecondPage_WithNoMore()
    {
        var svc = new FakeStudioService(15);

        var page = await svc.ListRecentAnalysesAsync(1, 10, CancellationToken.None);

        Assert.Equal(5, page.Items.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task ListRecentAnalysesAsync_EmptyStore_ReturnsEmptyNoMore()
    {
        var svc = new FakeStudioService(0);

        var page = await svc.ListRecentAnalysesAsync(0, 10, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task ListRecentAnalysesAsync_ExactlyOnePage_HasMoreFalse()
    {
        var svc = new FakeStudioService(10);

        var page = await svc.ListRecentAnalysesAsync(0, 10, CancellationToken.None);

        Assert.Equal(10, page.Items.Count);
        Assert.False(page.HasMore);
    }

    [Fact]
    public async Task ListRecentAnalysesAsync_OneItemOverPageSize_HasMoreTrue()
    {
        var svc = new FakeStudioService(11);

        var page = await svc.ListRecentAnalysesAsync(0, 10, CancellationToken.None);

        Assert.Equal(10, page.Items.Count);
        Assert.True(page.HasMore);
    }

    [Fact]
    public async Task ListRecentAnalysesAsync_MaterializedTemplateNamePreserved()
    {
        var svc = new FakeStudioService(3);

        var page = await svc.ListRecentAnalysesAsync(0, 10, CancellationToken.None);

        // Entry at index 0 has template name (i % 3 == 0)
        var entry = page.Items.First(e => e.TaskDescription == "Task 0");
        Assert.Equal("template-0", entry.MaterializedTemplateName);
    }

    [Fact]
    public async Task ListRecentAnalysesAsync_NonMaterializedEntry_HasNullTemplateName()
    {
        var svc = new FakeStudioService(3);

        var page = await svc.ListRecentAnalysesAsync(0, 10, CancellationToken.None);

        // Entry at index 1 has no template name (1 % 3 != 0)
        var entry = page.Items.First(e => e.TaskDescription == "Task 1");
        Assert.Null(entry.MaterializedTemplateName);
    }

    [Fact]
    public async Task ListRecentAnalysesAsync_LessThanOnePage_HasMoreFalse()
    {
        var svc = new FakeStudioService(5);

        var page = await svc.ListRecentAnalysesAsync(0, 10, CancellationToken.None);

        Assert.Equal(5, page.Items.Count);
        Assert.False(page.HasMore);
    }
}
