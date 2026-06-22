using Geef.Atelier.Application.Crew.Finalizers;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Learning;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Finalizers;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Finalizers;

/// <summary>
/// Verifies the recursion-safety guards in <see cref="LearningExtractFinalizerExecutor"/>:
/// the finalizer must skip Learning and CrewComposition runs without calling any downstream service.
/// Standard runs must NOT be skipped (the finalizer proceeds normally).
/// </summary>
public sealed class LearningExtractFinalizerRecursionGuardTests
{
    // ── Profile / context helpers ─────────────────────────────────────────

    private static FinalizerProfile MakeProfile() => new(
        Name:          "learning-extract",
        DisplayName:   "Learning Extract",
        Description:   "",
        FinalizerType: FinalizerType.LearningExtract,
        Settings: new Dictionary<string, string>
        {
            ["provider"]            = "stub",
            ["model"]               = "stub-model",
            ["minIterations"]       = "1",
            ["requireMajorFinding"] = "false",
        },
        IsSystem: true);

    private static FinalizerExecutionContext MakeContext(Guid runId) => new(
        RunId:          runId,
        TemplateName:   "test-template",
        FinalText:      "Final output.",
        CurrentText:    "Final output.",
        RunCompletedAt: DateTimeOffset.UtcNow);

    private static RunEntity MakeRun(Guid id, RunKind kind) => new RunEntity
    {
        Id               = id,
        CreatedAt        = DateTimeOffset.UtcNow,
        Status           = RunStatus.Completed,
        BriefingText     = "Briefing text",
        ConfigJson       = "{}",
        CreatedByUser    = "alice",
        Kind             = kind,
        CrewTemplateName = "akademisch",
    };

    private static IServiceScopeFactory BuildScopeFactory(
        IRunRepository runRepo,
        ILearningRepository learningRepo,
        IRunService runService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runRepo);
        services.AddSingleton(learningRepo);
        services.AddSingleton(runService);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static LearningExtractFinalizerExecutor MakeExecutor(
        IRunRepository runRepo,
        ILearningRepository learningRepo,
        IRunService runService)
    {
        var factory = BuildScopeFactory(runRepo, learningRepo, runService);
        return new LearningExtractFinalizerExecutor(
            new StubLlmClientResolver("Extracted insight."),
            factory,
            NullLogger<LearningExtractFinalizerExecutor>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LearningExtract_SkipsOnLearningRun()
    {
        // Arrange
        var runId        = Guid.NewGuid();
        var learningRepo = new CapturingLearningRepository();
        var runService   = new CapturingRunService();
        var executor     = MakeExecutor(
            new SingleRunRepository(MakeRun(runId, RunKind.Learning)),
            learningRepo,
            runService);

        // Act
        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId), default);

        // Assert: returned Ok without error artifact, without calling any downstream service
        Assert.Null(result.Artifact);
        Assert.False(learningRepo.CreateCalled, "CreateAsync must NOT be called for Learning runs.");
        Assert.False(runService.SubmitCalled, "SubmitRunAsync must NOT be called for Learning runs.");
    }

    [Fact]
    public async Task LearningExtract_SkipsOnCrewCompositionRun()
    {
        // Arrange
        var runId        = Guid.NewGuid();
        var learningRepo = new CapturingLearningRepository();
        var runService   = new CapturingRunService();
        var executor     = MakeExecutor(
            new SingleRunRepository(MakeRun(runId, RunKind.CrewComposition)),
            learningRepo,
            runService);

        // Act
        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId), default);

        // Assert: returned Ok without error artifact, without calling any downstream service
        Assert.Null(result.Artifact);
        Assert.False(learningRepo.CreateCalled, "CreateAsync must NOT be called for CrewComposition runs.");
        Assert.False(runService.SubmitCalled, "SubmitRunAsync must NOT be called for CrewComposition runs.");
    }

    [Fact]
    public async Task LearningExtract_DoesNotSkipOnStandardRun()
    {
        // Arrange: a standard run with enough iterations to pass the threshold
        var runId  = Guid.NewGuid();
        var run    = MakeRun(runId, RunKind.Standard);
        var iter1  = MakeIter(runId, 1);
        var iter2  = MakeIter(runId, 2);
        var details = new RunDetails(run, [iter1, iter2]);

        var learningRepo = new CapturingLearningRepository();
        var runService   = new CapturingRunService();
        var executor     = MakeExecutor(
            new SingleRunRepository(run, details),
            learningRepo,
            runService);

        // Act
        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId), default);

        // Assert: the finalizer PROCEEDED (it created an entry and submitted the learning run)
        Assert.True(learningRepo.CreateCalled, "CreateAsync MUST be called for Standard runs that pass the threshold.");
        Assert.True(runService.SubmitCalled, "SubmitRunAsync MUST be called for Standard runs that pass the threshold.");
        Assert.Null(result.Artifact); // no error artifact
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static IterationWithFindings MakeIter(Guid runId, int number) =>
        new IterationWithFindings(
            new IterationEntity
            {
                Id              = Guid.NewGuid(),
                RunId           = runId,
                IterationNumber = number,
                ArtifactText    = $"artifact {number}",
                CreatedAt       = DateTimeOffset.UtcNow,
            },
            []);

    // ── Fakes ─────────────────────────────────────────────────────────────

    private sealed class SingleRunRepository(RunEntity run, RunDetails? details = null) : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(runId == run.Id ? run : (RunEntity?)null);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(runId == run.Id ? details : null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([run]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken ct = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class CapturingLearningRepository : ILearningRepository
    {
        public bool CreateCalled { get; private set; }

        public Task<LearningEntry> CreateAsync(LearningEntry entry, float[] embedding, CancellationToken ct = default)
        {
            CreateCalled = true;
            return Task.FromResult(entry);
        }

        public Task SetLearningRunIdAsync(Guid id, Guid learningRunId, CancellationToken ct = default) => Task.CompletedTask;
        public Task<LearningEntry?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<LearningEntry?>(null);
        public Task<LearningEntry?> GetProposedBySourceRunIdAsync(Guid sourceRunId, CancellationToken ct = default) => Task.FromResult<LearningEntry?>(null);
        public Task UpdateStatusAsync(Guid id, LearningStatus status, DateTimeOffset? approvedAt, CancellationToken ct = default) => Task.CompletedTask;
        public Task SetEmbeddingAsync(Guid id, float[] embedding, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyList<LearningEntry>> ListAsync(LearningStatus? status = null, string? domain = null, string? owner = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<LearningEntry>>([]);
        public Task<IReadOnlyList<(LearningEntry Entry, double Similarity)>> SearchApprovedAsync(
            float[] queryEmbedding, string? currentDomain, double sameDomainBoost,
            double crossDomainPenalty, int topK, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(LearningEntry, double)>>([]);
        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class CapturingRunService : IRunService
    {
        public bool SubmitCalled { get; private set; }

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
        {
            SubmitCalled = true;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => Task.FromResult<RunEntity?>(null);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<RunEntity>>([]);
        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => Task.FromResult<RunDetails?>(null);
        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => Task.FromResult<RunWithGroundingViewModel?>(null);
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default) => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default) => Task.FromResult(Guid.NewGuid());
        public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class StubLlmClient(string response) : ILlmClient
    {
        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new LlmResponse
            {
                Text         = response,
                TokenUsage   = new LlmTokenUsage { InputTokens = 10, OutputTokens = 5 },
                FinishReason = "stop",
            });
    }

    private sealed class StubLlmClientResolver(string response) : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
            => (new StubLlmClient(response), "stub-model", 2048);

        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
            => (new StubLlmClient(response), model, maxTokens ?? 2048);

        public bool SupportsAgenticTools(string providerName) => true;
        public bool SupportsStructuredOutputs(string providerName) => true;
    }
}
