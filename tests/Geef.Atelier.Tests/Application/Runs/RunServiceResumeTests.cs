using System.Reflection;
using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Runs;

public sealed class RunServiceResumeTests
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    // ── Happy path: seed mode ─────────────────────────────────────────────

    [Fact]
    public async Task ResumeRunAsync_SeedMode_PassesSeedDraftFromLastIteration()
    {
        var parentId  = Guid.NewGuid();
        var iteration = MakeIteration(parentId, 1, "draft from iteration 1");
        var parent    = MakeRun(parentId, RunStatus.Aborted, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, [iteration]);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), "alice");

        Assert.Equal("draft from iteration 1", capturing.LastSeedDraftText);
        Assert.Equal(parentId, capturing.LastParentRunId);
    }

    [Fact]
    public async Task ResumeRunAsync_SeedMode_PrefersHighestIterationNumber()
    {
        var parentId  = Guid.NewGuid();
        var iter1     = MakeIteration(parentId, 1, "draft 1");
        var iter2     = MakeIteration(parentId, 2, "draft 2");
        var parent    = MakeRun(parentId, RunStatus.Aborted, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, [iter1, iter2]);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), "alice");

        Assert.Equal("draft 2", capturing.LastSeedDraftText);
    }

    [Fact]
    public async Task ResumeRunAsync_SeedMode_PrefersTheDeliveredTextOverTheLastIteration()
    {
        var parentId  = Guid.NewGuid();
        var iteration = MakeIteration(parentId, 2, "draft before the last corrections");
        var parent    = MakeRunWithText(parentId, RunStatus.Failed, "alice", "delivered text with open points");
        var capturing = new CapturingPersistenceService();
        var svc       = MakeService(capturing, new SingleDetailsRepository(parent, [iteration]));

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), "alice");

        Assert.Equal("delivered text with open points", capturing.LastSeedDraftText);
    }

    [Fact]
    public async Task ResumeRunAsync_SeedMode_DeliveredTextWithoutIterations_IsTheSeed()
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeRunWithText(parentId, RunStatus.Aborted, "alice", "stopped, last text");
        var capturing = new CapturingPersistenceService();
        var svc       = MakeService(capturing, new SingleDetailsRepository(parent, []));

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), "alice");

        Assert.Equal("stopped, last text", capturing.LastSeedDraftText);
    }

    [Fact]
    public async Task ResumeRunAsync_CleanRetry_IgnoresTheDeliveredText()
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeRunWithText(parentId, RunStatus.Failed, "alice", "delivered text");
        var capturing = new CapturingPersistenceService();
        var svc       = MakeService(capturing, new SingleDetailsRepository(parent, []));

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "alice");

        Assert.Null(capturing.LastSeedDraftText);
    }

    [Fact]
    public async Task ResumeRunAsync_SeedMode_NoIterations_SeedDraftIsNull()
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeRun(parentId, RunStatus.Aborted, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, []);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), "alice");

        Assert.Null(capturing.LastSeedDraftText);
    }

    // ── Happy path: clean retry ───────────────────────────────────────────

    [Fact]
    public async Task ResumeRunAsync_CleanRetry_SeedDraftIsNull()
    {
        var parentId  = Guid.NewGuid();
        var iteration = MakeIteration(parentId, 1, "draft");
        var parent    = MakeRun(parentId, RunStatus.Failed, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, [iteration]);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "alice");

        Assert.Null(capturing.LastSeedDraftText);
        Assert.Equal(parentId, capturing.LastParentRunId);
    }

    // ── RunKind inheritance ───────────────────────────────────────────────

    [Fact]
    public async Task ResumeRunAsync_RefusesAComposedRunAsARemovedWayOfWorking()
    {
        var parentId  = Guid.NewGuid();
        var parent    = new RunEntity
        {
            Id = parentId, CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Aborted,
            BriefingText = "briefing", ConfigJson = "{}", CreatedByUser = "alice",
            Kind = RunKind.CrewComposition,
            CrewTemplateName = Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplateName,
            CrewSnapshot = TestCrewSnapshots.RunnableJson,
        };
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, []);
        var svc       = MakeService(capturing, repo);

        var error = await Assert.ThrowsAsync<RunNotResumableException>(() =>
            svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "alice"));

        Assert.Equal(ResumeRules.RetiredSetupMessage, error.Message);
        Assert.Null(capturing.LastKind);
    }

    [Fact]
    public async Task ResumeRunAsync_InheritsParentRunKind_Standard()
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeRun(parentId, RunStatus.Failed, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, []);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "alice");

        Assert.Equal(RunKind.Standard, capturing.LastKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{\"schemaVersion\":99}")]
    public async Task ResumeRunAsync_ParentWithoutUsableSnapshot_IsRejectedAtOnceWithoutCreatingARun(string? snapshotJson)
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeRunWithSnapshot(parentId, RunStatus.Failed, "alice", snapshotJson);
        var capturing = new CapturingPersistenceService();
        var svc       = MakeService(capturing, new SingleDetailsRepository(parent, []));

        var ex = await Assert.ThrowsAsync<RunNotResumableException>(() =>
            svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "alice"));

        Assert.Equal("This task was never fully prepared and cannot be continued. Please submit it again.", ex.Message);
        Assert.Null(capturing.LastParentRunId);
    }

    [Theory]
    [InlineData(RunStatus.Failed, null)]
    [InlineData(RunStatus.Aborted, "alice")]
    public async Task ResumeRunAsync_ChangeThatAlreadyHasItsText_IsRejectedWithoutCreatingARun(RunStatus status, string? requestingUser)
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeOrderRun(parentId, status, "changed text with open points", 2, "Make it shorter.");
        var capturing = new CapturingPersistenceService();
        var svc       = MakeService(capturing, new SingleDetailsRepository(parent, []));

        var ex = await Assert.ThrowsAsync<RunNotResumableException>(() =>
            svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), requestingUser));

        Assert.Equal(Geef.Atelier.Application.Orders.RevisionLines.ResumeChangedText, ex.Message);
        Assert.Null(capturing.LastParentRunId);
    }

    [Fact]
    public async Task ResumeRunAsync_RunWithTextWithoutChange_IsResumedAsBefore()
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeOrderRun(parentId, RunStatus.Failed, "first text with open points", 1, null);
        var capturing = new CapturingPersistenceService();
        var svc       = MakeService(capturing, new SingleDetailsRepository(parent, []));

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: true, MaxIterationsOverride: null), "alice");

        Assert.Equal((parentId, "first text with open points"), (capturing.LastParentRunId, capturing.LastSeedDraftText));
    }

    // ── MaxIterationsOverride ─────────────────────────────────────────────

    [Fact]
    public async Task ResumeRunAsync_WithMaxIterationsOverride_PatchesConvergenceOverrideInSnapshot()
    {
        var parentId = Guid.NewGuid();
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: null,
            Executor: TestLoop.Writer,
            Reviewers: [TestLoop.Reviewers[0]],
            EvaluationStrategy: EvaluationStrategy.Parallel,
            ConvergenceOverride: null,
            Advisors: []);
        var snapshotJson = JsonSerializer.Serialize(snapshot, JsonOpts);
        var parent    = MakeRunWithSnapshot(parentId, RunStatus.Aborted, "alice", snapshotJson);
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, []);
        var svc       = MakeService(capturing, repo);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: 8), "alice");

        var newSnapshot = CrewSnapshot.Deserialize(capturing.LastSnapshotJson);
        Assert.NotNull(newSnapshot);
        Assert.Equal(8, newSnapshot!.ConvergenceOverride!.MaxIterations);
    }

    // ── Guards ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(RunStatus.Pending)]
    [InlineData(RunStatus.Running)]
    [InlineData(RunStatus.Completed)]
    public async Task ResumeRunAsync_NonResumableStatus_Throws(RunStatus status)
    {
        var parentId = Guid.NewGuid();
        var parent   = MakeRun(parentId, status, "alice");
        var repo     = new SingleDetailsRepository(parent, []);
        var svc      = MakeService(new CapturingPersistenceService(), repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "alice"));
    }

    [Fact]
    public async Task ResumeRunAsync_WrongOwner_Throws()
    {
        var parentId = Guid.NewGuid();
        var parent   = MakeRun(parentId, RunStatus.Aborted, "alice");
        var repo     = new SingleDetailsRepository(parent, []);
        var svc      = MakeService(new CapturingPersistenceService(), repo);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "bob"));
    }

    [Fact]
    public async Task ResumeRunAsync_NullUsername_BypassesOwnerCheck()
    {
        var parentId  = Guid.NewGuid();
        var parent    = MakeRun(parentId, RunStatus.Aborted, "alice");
        var capturing = new CapturingPersistenceService();
        var repo      = new SingleDetailsRepository(parent, []);
        var svc       = MakeService(capturing, repo);

        var result = await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), null);

        Assert.NotEqual(Guid.Empty, result);
    }

    [Fact]
    public async Task ResumeRunAsync_NotFound_Throws()
    {
        var svc = MakeService(new CapturingPersistenceService(), new EmptyRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.ResumeRunAsync(new ResumeOptions(Guid.NewGuid(), UseSeedDraft: false, MaxIterationsOverride: null), null));
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static RunService MakeService(IRunPersistenceService persistence, IRunRepository repo, IOrderRepository? orders = null) =>
        new RunService(
            persistence,
            repo,
            NullLogger<RunService>.Instance,
            new InMemoryRunWarnings(),
            orderRepository: orders);

    private static RunEntity MakeRun(Guid id, RunStatus status, string? createdByUser) =>
        new RunEntity
        {
            Id = id, CreatedAt = DateTimeOffset.UtcNow, Status = status,
            BriefingText = "briefing", ConfigJson = "{}", CreatedByUser = createdByUser,
            CrewTemplateName = Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplateName,
            CrewSnapshot = TestCrewSnapshots.RunnableJson,
        };

    private static RunEntity MakeRunWithText(Guid id, RunStatus status, string? createdByUser, string finalText) =>
        new RunEntity
        {
            Id = id, CreatedAt = DateTimeOffset.UtcNow, Status = status,
            BriefingText = "briefing", ConfigJson = "{}", CreatedByUser = createdByUser,
            CrewTemplateName = Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplateName, CrewSnapshot = TestCrewSnapshots.RunnableJson, FinalText = finalText,
        };

    private static RunEntity MakeOrderRun(Guid id, RunStatus status, string finalText, int version, string? revisionHint) =>
        new RunEntity
        {
            Id = id, CreatedAt = DateTimeOffset.UtcNow, Status = status,
            BriefingText = "briefing", ConfigJson = "{}", CreatedByUser = "alice",
            CrewTemplateName = Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplateName, CrewSnapshot = TestCrewSnapshots.RunnableJson, FinalText = finalText,
            OrderId = Guid.NewGuid(), OrderVersion = version, RevisionHint = revisionHint,
        };

    private static RunEntity MakeRunWithSnapshot(Guid id, RunStatus status, string? createdByUser, string? snapshotJson) =>
        new RunEntity
        {
            Id = id, CreatedAt = DateTimeOffset.UtcNow, Status = status,
            BriefingText = "briefing", ConfigJson = "{}", CreatedByUser = createdByUser,
            CrewTemplateName = Geef.Atelier.Core.Domain.Crew.SystemCrew.DirectTemplateName,
            CrewSnapshot = snapshotJson,
        };

    private static IterationWithFindings MakeIteration(Guid runId, int number, string text) =>
        new IterationWithFindings(
            new IterationEntity
            {
                Id = Guid.NewGuid(), RunId = runId, IterationNumber = number,
                ArtifactText = text, CreatedAt = DateTimeOffset.UtcNow,
            },
            []);

    [Fact]
    public async Task ResumeRunAsync_ReleasesTheRunOnlyAfterItIsLinkedToItsOrder()
    {
        var parentId  = Guid.NewGuid();
        var capturing = new CapturingPersistenceService();
        var orders    = DispatchProxy.Create<IOrderRepository, LinkRecorder>();
        ((LinkRecorder)(object)orders).Steps = capturing.Steps;
        var svc = MakeService(capturing, new SingleDetailsRepository(MakeRun(parentId, RunStatus.Failed, "alice"), []), orders);

        await svc.ResumeRunAsync(new ResumeOptions(parentId, UseSeedDraft: false, MaxIterationsOverride: null), "alice");

        Assert.Equal(["created held", "linked", "released"], capturing.Steps);
        Assert.NotNull(CrewSnapshot.Deserialize(capturing.LastSnapshotJson));
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    public class LinkRecorder : DispatchProxy
    {
        public List<string> Steps { get; set; } = [];

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name != nameof(IOrderRepository.LinkLegacyRunAsync))
                throw new NotSupportedException(targetMethod?.Name);
            Steps.Add("linked");
            return Task.FromResult(Guid.NewGuid());
        }
    }

    private sealed class CapturingPersistenceService : IRunPersistenceService
    {
        public Guid?    LastParentRunId   { get; private set; }
        public string?  LastSeedDraftText { get; private set; }
        public string?  LastSnapshotJson  { get; private set; }
        public RunKind? LastKind          { get; private set; }
        public List<string> Steps         { get; } = [];

        public Task<Guid> CreateRunAsync(string briefingText, string configJson,
            string? createdByUser = null, string? crewTemplateName = null,
            string? crewSnapshotJson = null, RunKind kind = RunKind.Standard,
            Guid? parentCompositionRunId = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> CreateResumedRunAsync(string briefingText, string configJson,
            string? createdByUser, string? crewTemplateName, string? crewSnapshotJson,
            Guid parentRunId, string? seedDraftText, RunKind kind = RunKind.Standard,
            CancellationToken cancellationToken = default)
        {
            LastParentRunId   = parentRunId;
            LastSeedDraftText = seedDraftText;
            LastSnapshotJson  = crewSnapshotJson;
            LastKind          = kind;
            Steps.Add(crewSnapshotJson is null ? "created held" : "created claimable");
            return Task.FromResult(Guid.NewGuid());
        }

        public Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default)
        {
            LastSnapshotJson = snapshotJson;
            Steps.Add("released");
            return Task.CompletedTask;
        }

        public Task MarkRunFailedAsync(Guid runId, string errorMessage, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SingleDetailsRepository(RunEntity run, IReadOnlyList<IterationWithFindings> iterations) : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(runId == run.Id ? run : (RunEntity?)null);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(runId == run.Id ? new RunDetails(run, iterations) : (RunDetails?)null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([run]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class EmptyRepository : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult<RunEntity?>(null);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult<RunDetails?>(null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
