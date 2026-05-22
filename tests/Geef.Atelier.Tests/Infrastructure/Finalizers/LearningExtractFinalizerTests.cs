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

public sealed class LearningExtractFinalizerTests
{
    // ── Profile / context helpers ─────────────────────────────────────────

    /// <summary>Creates a LearningExtract profile with given MinIterations (default 2).</summary>
    private static FinalizerProfile MakeProfile(int minIterations = 2, bool requireMajorFinding = false) => new(
        Name:          "learning-extract",
        DisplayName:   "Learning Extract",
        Description:   "",
        FinalizerType: FinalizerType.LearningExtract,
        Settings: new Dictionary<string, string>
        {
            ["provider"]           = "stub",
            ["model"]              = "stub-model",
            ["minIterations"]      = minIterations.ToString(),
            ["requireMajorFinding"] = requireMajorFinding.ToString().ToLowerInvariant(),
        },
        IsSystem: true);

    private static FinalizerExecutionContext MakeContext(Guid runId) => new(
        RunId:          runId,
        TemplateName:   "test-template",
        FinalText:      "Final output text.",
        CurrentText:    "Final output text.",
        RunCompletedAt: DateTimeOffset.UtcNow);

    private static RunEntity MakeRun(Guid id, RunKind kind = RunKind.Standard, string user = "alice") =>
        new RunEntity
        {
            Id              = id,
            CreatedAt       = DateTimeOffset.UtcNow,
            Status          = RunStatus.Completed,
            BriefingText    = "Briefing text for the run",
            ConfigJson      = "{}",
            CreatedByUser   = user,
            Kind            = kind,
            CrewTemplateName = "akademisch",
        };

    private static IterationWithFindings MakeIter(Guid runId, int number, FindingSeverity? severity = null)
    {
        IReadOnlyList<FindingEntity> findings = severity is null
            ? []
            : [new FindingEntity
              {
                  Id           = Guid.NewGuid(),
                  IterationId  = Guid.NewGuid(),
                  ReviewerName = "reviewer",
                  Severity     = severity.Value,
                  Message      = "test finding",
                  CreatedAt    = DateTimeOffset.UtcNow,
              }];

        return new IterationWithFindings(
            new IterationEntity
            {
                Id              = Guid.NewGuid(),
                RunId           = runId,
                IterationNumber = number,
                ArtifactText    = $"artifact {number}",
                CreatedAt       = DateTimeOffset.UtcNow,
            },
            findings);
    }

    /// <summary>
    /// Builds a real IServiceScopeFactory from a ServiceCollection containing the supplied fakes.
    /// The finalizer calls CreateAsyncScope() which is an extension on IServiceScopeFactory that
    /// wraps CreateScope(); using a real ServiceProvider is the simplest compatible approach.
    /// </summary>
    private static IServiceScopeFactory BuildScopeFactory(
        IRunRepository runRepo,
        ILearningRepository learningRepo,
        IRunService runService)
    {
        var services = new ServiceCollection();
        services.AddSingleton(runRepo);
        services.AddSingleton(learningRepo);
        services.AddSingleton(runService);
        var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IServiceScopeFactory>();
    }

    private static LearningExtractFinalizerExecutor MakeExecutor(
        IRunRepository runRepo,
        ILearningRepository learningRepo,
        IRunService runService,
        ILlmClientResolver? llmResolver = null)
    {
        llmResolver ??= new StubLlmClientResolver("Extracted learning insight.");
        var factory = BuildScopeFactory(runRepo, learningRepo, runService);
        return new LearningExtractFinalizerExecutor(
            llmResolver,
            factory,
            NullLogger<LearningExtractFinalizerExecutor>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecursionGuard_LearningRun_ReturnsOkWithoutCreatingEntry()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunKind.Learning);
        var learningRepo = new CapturingLearningRepository([]);
        var executor    = MakeExecutor(
            new StubRunRepository(run, new RunDetails(run, [])),
            learningRepo,
            new StubRunService());

        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId), default);

        Assert.Null(result.Artifact);
        Assert.False(learningRepo.CreateCalled);
    }

    [Fact]
    public async Task ThresholdMiss_OneIterNoMajorFinding_ReturnsOkWithoutCreatingEntry()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunKind.Standard);
        var details     = new RunDetails(run, [MakeIter(runId, 1, FindingSeverity.Minor)]);
        var learningRepo = new CapturingLearningRepository([]);
        // MinIterations=2, RequireMajorFinding=false means:
        // threshold passes if iterCount >= 2 OR (hasSignificant && !requireMajorFinding)
        // iterCount=1, no significant finding → threshold miss
        var executor = MakeExecutor(
            new StubRunRepository(run, details),
            learningRepo,
            new StubRunService());

        var result = await executor.ExecuteAsync(MakeProfile(minIterations: 2), MakeContext(runId), default);

        Assert.Null(result.Artifact);
        Assert.False(learningRepo.CreateCalled);
    }

    [Fact]
    public async Task ThresholdHitViaIterations_TwoIters_CreatesEntryAndFiresSubmit()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunKind.Standard);
        var details     = new RunDetails(run, [MakeIter(runId, 1), MakeIter(runId, 2)]);
        var learningRepo = new CapturingLearningRepository([]);
        var runService  = new CapturingRunService();
        var executor    = MakeExecutor(
            new StubRunRepository(run, details),
            learningRepo,
            runService);

        var result = await executor.ExecuteAsync(MakeProfile(minIterations: 2), MakeContext(runId), default);

        Assert.True(learningRepo.CreateCalled, "CreateAsync should have been called.");
        Assert.True(runService.SubmitCalled, "SubmitRunAsync should have been called for the learning run.");
        Assert.Null(result.Artifact);  // no error artifact
    }

    [Fact]
    public async Task ThresholdHitViaMajorFinding_OneIterWithMajor_CreatesEntryAndFiresSubmit()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunKind.Standard);
        var details     = new RunDetails(run, [MakeIter(runId, 1, FindingSeverity.Major)]);
        var learningRepo = new CapturingLearningRepository([]);
        var runService  = new CapturingRunService();
        // MinIterations=2 but hasSignificant=true and !requireMajorFinding → threshold passes
        var executor = MakeExecutor(
            new StubRunRepository(run, details),
            learningRepo,
            runService,
            new StubLlmClientResolver("Insight via major finding."));

        var result = await executor.ExecuteAsync(
            MakeProfile(minIterations: 2, requireMajorFinding: false), MakeContext(runId), default);

        Assert.True(learningRepo.CreateCalled);
        Assert.True(runService.SubmitCalled);
        Assert.Null(result.Artifact);
    }

    [Fact]
    public async Task ThresholdHitViaCriticalFinding_CriticalIsBelowMajor_CreatesEntry()
    {
        // Critical < Major in the enum, so it qualifies as "significant"
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunKind.Standard);
        var details     = new RunDetails(run, [MakeIter(runId, 1, FindingSeverity.Critical)]);
        var learningRepo = new CapturingLearningRepository([]);
        var runService  = new CapturingRunService();
        var executor    = MakeExecutor(
            new StubRunRepository(run, details),
            learningRepo,
            runService);

        var result = await executor.ExecuteAsync(
            MakeProfile(minIterations: 2, requireMajorFinding: false), MakeContext(runId), default);

        Assert.True(learningRepo.CreateCalled);
        Assert.True(runService.SubmitCalled);
    }

    [Fact]
    public async Task FireAndForgetResilience_SubmitThrows_OriginalRunStillOk()
    {
        var runId       = Guid.NewGuid();
        var run         = MakeRun(runId, RunKind.Standard);
        var details     = new RunDetails(run, [MakeIter(runId, 1), MakeIter(runId, 2)]);
        var learningRepo = new CapturingLearningRepository([]);
        var runService  = new ThrowingRunService();
        var executor    = MakeExecutor(
            new StubRunRepository(run, details),
            learningRepo,
            runService);

        // Should not throw even though SubmitRunAsync throws
        var result = await executor.ExecuteAsync(MakeProfile(minIterations: 2), MakeContext(runId), default);

        // Entry should still have been created
        Assert.True(learningRepo.CreateCalled, "Entry should be created even if submit fails.");
        // No error artifact because the submit failure is swallowed
        Assert.Null(result.Artifact);
    }

    [Fact]
    public async Task RunNotFound_ReturnsOkWithoutError()
    {
        var runId    = Guid.NewGuid();
        var executor = MakeExecutor(
            new StubRunRepository(null, null),
            new CapturingLearningRepository([]),
            new StubRunService());

        var result = await executor.ExecuteAsync(MakeProfile(), MakeContext(runId), default);

        Assert.Null(result.Artifact);
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    private sealed class StubRunRepository(RunEntity? run, RunDetails? details) : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(run is null ? null : runId == run.Id ? run : null);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(details is null ? null : runId == details.Run.Id ? details : null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>(run is null ? [] : [run]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken ct = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class CapturingLearningRepository(IReadOnlyList<LearningEntry> entries) : ILearningRepository
    {
        public bool CreateCalled { get; private set; }
        public bool SetLearningRunIdCalled { get; private set; }

        public Task<LearningEntry> CreateAsync(LearningEntry entry, float[] embedding, CancellationToken ct = default)
        {
            CreateCalled = true;
            return Task.FromResult(entry);
        }

        public Task SetLearningRunIdAsync(Guid id, Guid learningRunId, CancellationToken ct = default)
        {
            SetLearningRunIdCalled = true;
            return Task.CompletedTask;
        }

        public Task<LearningEntry?> GetByIdAsync(Guid id, CancellationToken ct = default)
            => Task.FromResult<LearningEntry?>(null);

        public Task<LearningEntry?> GetProposedBySourceRunIdAsync(Guid sourceRunId, CancellationToken ct = default)
            => Task.FromResult<LearningEntry?>(null);

        public Task UpdateStatusAsync(Guid id, LearningStatus status, DateTimeOffset? approvedAt, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task SetEmbeddingAsync(Guid id, float[] embedding, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<LearningEntry>> ListAsync(LearningStatus? status = null, string? domain = null, string? owner = null, CancellationToken ct = default)
            => Task.FromResult(entries.Where(e => status is null || e.Status == status).ToList() as IReadOnlyList<LearningEntry>);

        public Task<IReadOnlyList<(LearningEntry Entry, double Similarity)>> SearchApprovedAsync(
            float[] queryEmbedding, string? currentDomain, double sameDomainBoost,
            double crossDomainPenalty, int topK, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<(LearningEntry, double)>>([]);

        public Task DeleteAsync(Guid id, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class StubRunService : IRunService
    {
        public bool SubmitCalled { get; private set; }

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
        {
            SubmitCalled = true;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([]);
        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunDetails?>(null);
        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunWithGroundingViewModel?>(null);
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());
        public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class CapturingRunService : IRunService
    {
        public bool SubmitCalled { get; private set; }

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
        {
            SubmitCalled = true;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([]);
        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunDetails?>(null);
        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunWithGroundingViewModel?>(null);
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());
        public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed class ThrowingRunService : IRunService
    {
        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Submit deliberately failed for test.");

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([]);
        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunDetails?>(null);
        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunWithGroundingViewModel?>(null);
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());
        public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
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
    }
}
