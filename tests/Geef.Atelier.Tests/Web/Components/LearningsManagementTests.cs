using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Crew.Learning;
using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class LearningsManagementTests : TestContext
{
    private static LearningEntry MakeEntry(
        LearningStatus status = LearningStatus.Proposed,
        string owner = "stefan") =>
        new LearningEntry(
            Id:                  Guid.NewGuid(),
            Text:                "An extracted learning insight for testing purposes.",
            SourceRunId:         Guid.NewGuid(),
            LearningRunId:       null,
            Domain:              "akademisch",
            Status:              status,
            StructuredFactsJson: "{\"key\":\"value\"}",
            OwnerUsername:       owner,
            CreatedAt:           DateTimeOffset.UtcNow,
            ApprovedAt:          status == LearningStatus.Approved ? DateTimeOffset.UtcNow : null);

    private void SetupAuthorized(ILearningService svc, string user = "stefan")
    {
        Services.AddSingleton(svc);
        this.AddTestAuthorization().SetAuthorized(user);
    }

    // ── Page structure ────────────────────────────────────────────────────

    [Fact]
    public void Page_RendersLearningsIndexTestId()
    {
        SetupAuthorized(new StubLearningService([]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='learnings-index']");
    }

    // ── Empty state ───────────────────────────────────────────────────────

    [Fact]
    public void EmptyState_ShowsEmptyStateTestId()
    {
        SetupAuthorized(new StubLearningService([]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='empty-state']");
    }

    [Fact]
    public void EmptyState_DoesNotShowLearningCard()
    {
        SetupAuthorized(new StubLearningService([]));
        var cut = RenderComponent<LearningsIndex>();
        Assert.Empty(cut.FindAll("[data-testid='learning-card']"));
    }

    // ── With entries ──────────────────────────────────────────────────────

    [Fact]
    public void WithEntries_RendersManyLearningCards()
    {
        var entries = new[] { MakeEntry(), MakeEntry(LearningStatus.Approved) };
        SetupAuthorized(new StubLearningService(entries));
        var cut = RenderComponent<LearningsIndex>();
        Assert.Equal(2, cut.FindAll("[data-testid='learning-card']").Count);
    }

    [Fact]
    public void WithEntry_RendersStatusBadge()
    {
        SetupAuthorized(new StubLearningService([MakeEntry()]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='status-badge']");
    }

    [Fact]
    public void WithEntry_RendersDeleteButton()
    {
        SetupAuthorized(new StubLearningService([MakeEntry()]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='btn-delete']");
    }

    // ── Approve / Reject button visibility ────────────────────────────────

    [Fact]
    public void ProposedEntry_ShowsApproveButton()
    {
        SetupAuthorized(new StubLearningService([MakeEntry(LearningStatus.Proposed)]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='btn-approve']");
    }

    [Fact]
    public void ProposedEntry_ShowsRejectButton()
    {
        SetupAuthorized(new StubLearningService([MakeEntry(LearningStatus.Proposed)]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='btn-reject']");
    }

    [Fact]
    public void ApprovedEntry_DoesNotShowApproveButton()
    {
        SetupAuthorized(new StubLearningService([MakeEntry(LearningStatus.Approved)]));
        var cut = RenderComponent<LearningsIndex>();
        Assert.Empty(cut.FindAll("[data-testid='btn-approve']"));
    }

    [Fact]
    public void ApprovedEntry_ShowsRejectButton()
    {
        SetupAuthorized(new StubLearningService([MakeEntry(LearningStatus.Approved)]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='btn-reject']");
    }

    [Fact]
    public void RejectedEntry_ShowsApproveButton()
    {
        SetupAuthorized(new StubLearningService([MakeEntry(LearningStatus.Rejected)]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='btn-approve']");
    }

    [Fact]
    public void RejectedEntry_DoesNotShowRejectButton()
    {
        SetupAuthorized(new StubLearningService([MakeEntry(LearningStatus.Rejected)]));
        var cut = RenderComponent<LearningsIndex>();
        Assert.Empty(cut.FindAll("[data-testid='btn-reject']"));
    }

    // ── Status badge content ──────────────────────────────────────────────

    [Fact]
    public void ProposedEntry_StatusBadgeHasProposedClass()
    {
        SetupAuthorized(new StubLearningService([MakeEntry(LearningStatus.Proposed)]));
        var cut   = RenderComponent<LearningsIndex>();
        var badge = cut.Find("[data-testid='status-badge']");
        Assert.Contains("proposed", badge.ClassName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApprovedEntry_StatusBadgeHasApprovedClass()
    {
        SetupAuthorized(new StubLearningService([MakeEntry(LearningStatus.Approved)]));
        var cut   = RenderComponent<LearningsIndex>();
        var badge = cut.Find("[data-testid='status-badge']");
        Assert.Contains("approved", badge.ClassName, StringComparison.OrdinalIgnoreCase);
    }

    // ── Filter bar ────────────────────────────────────────────────────────

    [Fact]
    public void Page_RendersFilterBar()
    {
        SetupAuthorized(new StubLearningService([]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='filter-bar']");
    }

    [Fact]
    public void Page_RendersStatusFilterDropdown()
    {
        SetupAuthorized(new StubLearningService([]));
        var cut = RenderComponent<LearningsIndex>();
        cut.Find("[data-testid='status-filter']");
    }

    // ── Fake ──────────────────────────────────────────────────────────────

    private sealed class StubLearningService(IReadOnlyList<LearningEntry> entries) : ILearningService
    {
        public Task<IReadOnlyList<LearningEntry>> ListAsync(
            LearningStatus? status = null,
            string? domain = null,
            CancellationToken ct = default)
        {
            var result = entries
                .Where(e => status is null || e.Status == status)
                .ToList();
            return Task.FromResult<IReadOnlyList<LearningEntry>>(result);
        }

        public Task<LearningEntry?> GetAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(entries.FirstOrDefault(e => e.Id == id));

        public Task ApproveAsync(Guid id, string requestingUsername, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RejectAsync(Guid id, string requestingUsername, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeleteAsync(Guid id, string requestingUsername, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
