using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Finalizers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Finalizers;

public sealed class LearningPublishFinalizerTests
{
    // ── Profile / context helpers ─────────────────────────────────────────

    private static FinalizerProfile MakeProfile() => new(
        Name:          "learning-publish",
        DisplayName:   "Learning Publish",
        Description:   "",
        FinalizerType: FinalizerType.LearningPublish,
        Settings:      [],
        IsSystem:      true);

    private static FinalizerExecutionContext MakeContext(Guid runId, string finalText = "Final text.") => new(
        RunId:          runId,
        TemplateName:   "learning-evaluation",
        FinalText:      finalText,
        CurrentText:    finalText,
        RunCompletedAt: DateTimeOffset.UtcNow);

    private static RunEntity MakeRun(Guid id, RunStatus status, RunKind kind) =>
        new RunEntity
        {
            Id            = id,
            CreatedAt     = DateTimeOffset.UtcNow,
            Status        = status,
            BriefingText  = "Learning evaluation briefing",
            ConfigJson    = "{}",
            CreatedByUser = "system",
            Kind          = kind,
        };

    private static LearningEntry MakeEntry(Guid learningRunId) => new LearningEntry(
        Id:                  Guid.NewGuid(),
        Text:                "Candidate insight",
        SourceRunId:         Guid.NewGuid(),
        LearningRunId:       learningRunId,
        Domain:              "akademisch",
        Status:              LearningStatus.Proposed,
        StructuredFactsJson: "{}",
        OwnerUsername:       "alice",
        CreatedAt:           DateTimeOffset.UtcNow,
        ApprovedAt:          null);

    private static IServiceScopeFactory BuildScopeFactory(
        IRunRepository runRepo,
        ILearningRepository learningRepo,
        IEmbeddingProvider? embeddingProvider = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runRepo);
        services.AddSingleton(learningRepo);
        if (embeddingProvider is not null)
            services.AddSingleton(embeddingProvider);
        else
            services.AddSingleton<IEmbeddingProvider>(new StubEmbeddingProvider());
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static LearningPublishFinalizerExecutor MakeExecutor(
        IRunRepository runRepo,
        ILearningRepository learningRepo,
        IEmbeddingProvider? embeddingProvider = null)
    {
        var factory = BuildScopeFactory(runRepo, learningRepo, embeddingProvider);
        return new LearningPublishFinalizerExecutor(
            factory,
            NullLogger<LearningPublishFinalizerExecutor>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecursionGuard_StandardRun_ReturnsOkWithoutStatusChange()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunStatus.Completed, RunKind.Standard);
        var learningRepo = new CapturingLearningRepository([]);
        var executor    = MakeExecutor(
            new StubRunRepository(run),
            learningRepo);

        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId), default);

        Assert.Null(result.Artifact);
        Assert.Null(learningRepo.LastStatus);
    }

    [Fact]
    public async Task ApprovedPath_LearningRunCompleted_SetsApprovedStatus()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunStatus.Completed, RunKind.Learning);
        var entry       = MakeEntry(runId);
        var learningRepo = new CapturingLearningRepository([entry]);
        var executor    = MakeExecutor(
            new StubRunRepository(run),
            learningRepo);

        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId, "Non-empty final text"), default);

        Assert.Null(result.Artifact);
        Assert.Equal(LearningStatus.Approved, learningRepo.LastStatus);
        Assert.NotNull(learningRepo.LastApprovedAt);
    }

    [Fact]
    public async Task RejectedPath_LearningRunFailed_SetsRejectedStatus()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunStatus.Failed, RunKind.Learning);
        var entry       = MakeEntry(runId);
        var learningRepo = new CapturingLearningRepository([entry]);
        var executor    = MakeExecutor(
            new StubRunRepository(run),
            learningRepo);

        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId), default);

        Assert.Null(result.Artifact);
        Assert.Equal(LearningStatus.Rejected, learningRepo.LastStatus);
        Assert.Null(learningRepo.LastApprovedAt);
    }

    [Fact]
    public async Task RejectedPath_LearningRunAborted_SetsRejectedStatus()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunStatus.Aborted, RunKind.Learning);
        var entry       = MakeEntry(runId);
        var learningRepo = new CapturingLearningRepository([entry]);
        var executor    = MakeExecutor(
            new StubRunRepository(run),
            learningRepo);

        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId), default);

        Assert.Equal(LearningStatus.Rejected, learningRepo.LastStatus);
    }

    [Fact]
    public async Task NoEntryFound_ReturnsOkWithoutCrash()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunStatus.Completed, RunKind.Learning);
        // No entry with matching LearningRunId
        var learningRepo = new CapturingLearningRepository([]);
        var executor    = MakeExecutor(
            new StubRunRepository(run),
            learningRepo);

        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId, "text"), default);

        Assert.Null(result.Artifact);
        Assert.Null(learningRepo.LastStatus);
    }

    [Fact]
    public async Task RunNotFound_ReturnsOk()
    {
        var runId    = Guid.NewGuid();
        var executor = MakeExecutor(
            new StubRunRepository(null),
            new CapturingLearningRepository([]));

        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId), default);

        Assert.Null(result.Artifact);
    }

    [Fact]
    public async Task ApprovedPath_CompletedWithEmptyFinalText_SetsRejected()
    {
        // run.Status==Completed but FinalText is empty → falls to Reject branch
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunStatus.Completed, RunKind.Learning);
        var entry       = MakeEntry(runId);
        var learningRepo = new CapturingLearningRepository([entry]);
        var executor    = MakeExecutor(
            new StubRunRepository(run),
            learningRepo);

        // FinalText is whitespace → goes to Rejected path
        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId, "   "), default);

        Assert.Equal(LearningStatus.Rejected, learningRepo.LastStatus);
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    private sealed class StubRunRepository(RunEntity? run) : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(run is null ? null : runId == run.Id ? run : null);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult<RunDetails?>(null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>(run is null ? [] : [run]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken ct = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>Tracks UpdateStatusAsync calls; supports listing by LearningRunId match.</summary>
    private sealed class CapturingLearningRepository(IReadOnlyList<LearningEntry> entries) : ILearningRepository
    {
        public LearningStatus? LastStatus    { get; private set; }
        public DateTimeOffset? LastApprovedAt { get; private set; }

        public Task<LearningEntry> CreateAsync(LearningEntry entry, float[] embedding, CancellationToken ct = default)
            => Task.FromResult(entry);

        public Task<LearningEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<LearningEntry?>(null);

        public Task<LearningEntry?> GetProposedBySourceRunIdAsync(Guid sourceRunId, CancellationToken ct = default)
            => Task.FromResult<LearningEntry?>(null);

        public Task UpdateStatusAsync(Guid id, LearningStatus status, DateTimeOffset? approvedAt, CancellationToken ct = default)
        {
            LastStatus    = status;
            LastApprovedAt = approvedAt;
            return Task.CompletedTask;
        }

        public Task SetLearningRunIdAsync(Guid id, Guid learningRunId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetEmbeddingAsync(Guid id, float[] embedding, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<LearningEntry>> ListAsync(
            LearningStatus? status = null,
            string? domain = null,
            string? owner = null,
            CancellationToken ct = default)
        {
            var result = entries
                .Where(e => status is null || e.Status == status)
                .ToList();
            return Task.FromResult<IReadOnlyList<LearningEntry>>(result);
        }

        public Task<IReadOnlyList<(LearningEntry Entry, double Similarity)>> SearchApprovedAsync(
            float[] queryEmbedding,
            string? currentDomain,
            double sameDomainBoost,
            double crossDomainPenalty,
            int topK,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(LearningEntry, double)>>([]);

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubEmbeddingProvider : IEmbeddingProvider
    {
        public string ProviderName => "stub";
        public string ModelName    => "stub-embedding";
        public int Dimensions      => 4;

        public Task<EmbeddingResult> CreateAsync(string text, CancellationToken ct)
            => Task.FromResult(new EmbeddingResult(new float[] { 0.1f, 0.2f, 0.3f, 0.4f }, 5, null));

        public Task<IReadOnlyList<EmbeddingResult>> CreateBatchAsync(IReadOnlyList<string> texts, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<EmbeddingResult>>(
                texts.Select(_ => new EmbeddingResult(new float[] { 0.1f, 0.2f, 0.3f, 0.4f }, 5, null)).ToList());
    }
}
