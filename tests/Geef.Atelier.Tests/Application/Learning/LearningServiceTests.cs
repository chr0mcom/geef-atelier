using Geef.Atelier.Application.Crew.Learning;
using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Tests.Application.Learning;

public sealed class LearningServiceTests
{
    private static LearningEntry MakeEntry(string owner, LearningStatus status = LearningStatus.Proposed) =>
        new LearningEntry(
            Id:                  Guid.NewGuid(),
            Text:                "An insight.",
            SourceRunId:         Guid.NewGuid(),
            LearningRunId:       null,
            Domain:              "test",
            Status:              status,
            StructuredFactsJson: "{}",
            OwnerUsername:       owner,
            CreatedAt:           DateTimeOffset.UtcNow,
            ApprovedAt:          null);

    // ── ApproveAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAsync_CorrectOwner_SetsApprovedStatus()
    {
        var entry  = MakeEntry("alice");
        var repo   = new CapturingLearningRepository(entry);
        var svc    = new LearningService(repo);

        await svc.ApproveAsync(entry.Id, "alice");

        Assert.Equal(LearningStatus.Approved, repo.LastStatus);
        Assert.NotNull(repo.LastApprovedAt);
    }

    [Fact]
    public async Task ApproveAsync_WrongOwner_ThrowsInvalidOperationException()
    {
        var entry = MakeEntry("alice");
        var repo  = new CapturingLearningRepository(entry);
        var svc   = new LearningService(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ApproveAsync(entry.Id, "bob"));
    }

    // ── RejectAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task RejectAsync_CorrectOwner_SetsRejectedStatus()
    {
        var entry = MakeEntry("alice");
        var repo  = new CapturingLearningRepository(entry);
        var svc   = new LearningService(repo);

        await svc.RejectAsync(entry.Id, "alice");

        Assert.Equal(LearningStatus.Rejected, repo.LastStatus);
        Assert.Null(repo.LastApprovedAt);
    }

    [Fact]
    public async Task RejectAsync_WrongOwner_ThrowsInvalidOperationException()
    {
        var entry = MakeEntry("alice");
        var repo  = new CapturingLearningRepository(entry);
        var svc   = new LearningService(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RejectAsync(entry.Id, "charlie"));
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_CorrectOwner_CallsDelete()
    {
        var entry = MakeEntry("alice");
        var repo  = new CapturingLearningRepository(entry);
        var svc   = new LearningService(repo);

        await svc.DeleteAsync(entry.Id, "alice");

        Assert.True(repo.DeleteCalled);
    }

    [Fact]
    public async Task DeleteAsync_WrongOwner_ThrowsInvalidOperationException()
    {
        var entry = MakeEntry("alice");
        var repo  = new CapturingLearningRepository(entry);
        var svc   = new LearningService(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DeleteAsync(entry.Id, "mallory"));
    }

    [Fact]
    public async Task DeleteAsync_EntryNotFound_ThrowsInvalidOperationException()
    {
        var repo = new CapturingLearningRepository(null);
        var svc  = new LearningService(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DeleteAsync(Guid.NewGuid(), "alice"));
    }

    // ── Fake ──────────────────────────────────────────────────────────────

    private sealed class CapturingLearningRepository(LearningEntry? entry) : ILearningRepository
    {
        public LearningStatus? LastStatus    { get; private set; }
        public DateTimeOffset? LastApprovedAt { get; private set; }
        public bool            DeleteCalled  { get; private set; }

        public Task<LearningEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult(entry is null ? null : id == entry.Id ? entry : null);

        public Task UpdateStatusAsync(Guid id, LearningStatus status, DateTimeOffset? approvedAt, CancellationToken ct = default)
        {
            LastStatus    = status;
            LastApprovedAt = approvedAt;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        {
            DeleteCalled = true;
            return Task.CompletedTask;
        }

        public Task<LearningEntry> CreateAsync(LearningEntry e, float[] embedding, CancellationToken ct = default)
            => Task.FromResult(e);

        public Task<LearningEntry?> GetProposedBySourceRunIdAsync(Guid sourceRunId, CancellationToken ct = default)
            => Task.FromResult<LearningEntry?>(null);

        public Task SetLearningRunIdAsync(Guid id, Guid learningRunId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetEmbeddingAsync(Guid id, float[] embedding, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<LearningEntry>> ListAsync(
            LearningStatus? status = null,
            string? domain = null,
            string? owner = null,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LearningEntry>>(entry is null ? [] : [entry]);

        public Task<IReadOnlyList<(LearningEntry Entry, double Similarity)>> SearchApprovedAsync(
            float[] queryEmbedding,
            string? currentDomain,
            double sameDomainBoost,
            double crossDomainPenalty,
            int topK,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(LearningEntry, double)>>([]);
    }
}
