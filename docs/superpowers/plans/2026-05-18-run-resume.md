# Run fortsetzen (Resume Run) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Fortsetzen" button to Aborted/Failed runs that creates a child run, optionally seeding its first pipeline iteration with the last completed draft.

**Architecture:** Two new nullable columns on `Runs` (`ParentRunId`, `SeedDraftText`). `RunService.ResumeRunAsync` clones the parent run, optionally patching `MaxIterations` in the CrewSnapshot. The orchestrator detects `SeedDraftText != null` and dispatches `AtelierPipelineFactory.BuildWithSeedDraft`, which injects the seed via a new `SeedDraftGroundingStep`. On iteration 1, `ProfileBasedExecutor` switches to a "revise this draft" prompt instead of "write from scratch". A new `ResumeRunDialog` modal in `RunDetail.razor` collects the mode and iteration budget from the user.

**Tech Stack:** .NET 10, Blazor Server, EF Core + Npgsql, xUnit + bUnit, Geef.Sdk (IGroundingStep, IRunContext, ContextKey)

**Design spec:** [`docs/design/run-resume-design.md`](../../design/run-resume-design.md)

---

## File Map

**New files:**
- `src/Geef.Atelier.Application/Runs/ResumeOptions.cs`
- `src/Geef.Atelier.Infrastructure/Pipeline/SeedDraftGroundingStep.cs`
- `src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260523120000_Step23RunResume.cs`
- `src/Geef.Atelier.Web/Components/UI/ResumeRunDialog.razor`
- `tests/Geef.Atelier.Tests/Application/Runs/RunServiceResumeTests.cs`
- `tests/Geef.Atelier.Tests/Infrastructure/Pipeline/SeedDraftGroundingStepTests.cs`
- `tests/Geef.Atelier.Tests/Infrastructure/Pipeline/ProfileBasedExecutorSeedDraftTests.cs`
- `tests/Geef.Atelier.Tests/Web/Components/ResumeRunDialogTests.cs`

**Modified files:**
- `src/Geef.Atelier.Core/Domain/RunEntity.cs` — `ParentRunId`, `SeedDraftText`
- `src/Geef.Atelier.Core/Persistence/IRunPersistenceService.cs` — `CreateResumedRunAsync`
- `src/Geef.Atelier.Application/Runs/IRunService.cs` — `ResumeRunAsync`
- `src/Geef.Atelier.Application/Runs/RunService.cs` — implement `ResumeRunAsync`
- `src/Geef.Atelier.Infrastructure/Persistence/RunPersistenceService.cs` — implement `CreateResumedRunAsync`
- `src/Geef.Atelier.Infrastructure/Persistence/Configurations/RunConfiguration.cs` — new column config
- `src/Geef.Atelier.Infrastructure/Pipeline/AtelierContextKeys.cs` — `SeedDraft` key
- `src/Geef.Atelier.Infrastructure/Pipeline/AtelierPipelineFactory.cs` — `BuildWithSeedDraft`
- `src/Geef.Atelier.Infrastructure/Pipeline/ProfileBasedExecutor.cs` — iter-1 seed-draft branch
- `src/Geef.Atelier.Web/Services/RunOrchestratorService.cs` — dispatch to `BuildWithSeedDraft`
- `src/Geef.Atelier.Web/Components/Pages/RunDetail.razor` — button + parent link + dialog wire-up
- `tests/Geef.Atelier.Tests/Application/Runs/RunServiceUserIsolationTests.cs` — stub update
- `tests/Geef.Atelier.Tests/Application/Runs/RunServiceAttachmentTests.cs` — stub update

---

## Task 1: Data Layer — RunEntity columns + EF config + migration

**Files:**
- Modify: `src/Geef.Atelier.Core/Domain/RunEntity.cs`
- Modify: `src/Geef.Atelier.Infrastructure/Persistence/Configurations/RunConfiguration.cs`
- Create: `src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260523120000_Step23RunResume.cs`

- [ ] **Step 1: Add properties to RunEntity**

Open `src/Geef.Atelier.Core/Domain/RunEntity.cs`. After the `FinalizerErrorMessage` property add:

```csharp
    /// <summary>
    /// Set when this run was created as a resume of an existing run.
    /// The parent run is never modified; its status stays Aborted or Failed.
    /// </summary>
    public Guid? ParentRunId { get; init; }

    /// <summary>
    /// The artifact text of the last completed iteration of the parent run,
    /// injected as seed draft for the first pipeline iteration.
    /// Null for clean-retry resumes and for runs that were not resumed.
    /// </summary>
    public string? SeedDraftText { get; init; }
```

- [ ] **Step 2: Add EF configuration for the new columns**

Open `src/Geef.Atelier.Infrastructure/Persistence/Configurations/RunConfiguration.cs`. After the `HasIndex(r => r.Status)` line add:

```csharp
        builder.Property(r => r.ParentRunId).IsRequired(false);
        builder.Property(r => r.SeedDraftText).IsRequired(false);
        builder.HasIndex(r => r.ParentRunId).HasDatabaseName("IX_Runs_ParentRunId");
```

- [ ] **Step 3: Create migration file**

Create `src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260523120000_Step23RunResume.cs`:

```csharp
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260523120000_Step23RunResume")]
    public partial class Step23RunResume : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""Runs"" ADD COLUMN IF NOT EXISTS ""ParentRunId"" uuid NULL;
ALTER TABLE ""Runs"" ADD COLUMN IF NOT EXISTS ""SeedDraftText"" text NULL;
CREATE INDEX IF NOT EXISTS ""IX_Runs_ParentRunId"" ON ""Runs""(""ParentRunId"");");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP INDEX IF EXISTS ""IX_Runs_ParentRunId"";
ALTER TABLE ""Runs"" DROP COLUMN IF EXISTS ""ParentRunId"";
ALTER TABLE ""Runs"" DROP COLUMN IF EXISTS ""SeedDraftText"";");
        }
    }
}
```

- [ ] **Step 4: Update ModelSnapshot**

Open `src/Geef.Atelier.Infrastructure/Persistence/Migrations/AtelierDbContextModelSnapshot.cs`. Find the `Runs` table builder block and add after the existing property lines (before the closing of the `Runs` entity block):

```csharp
                    b.Property<Guid?>("ParentRunId")
                        .HasColumnType("uuid");

                    b.Property<string>("SeedDraftText")
                        .HasColumnType("text");

                    b.HasIndex("ParentRunId")
                        .HasDatabaseName("IX_Runs_ParentRunId");
```

- [ ] **Step 5: Build to verify — 0 errors**

```bash
cd /srv/docker/websites/geef_atelier
dotnet build
```
Expected: `Build succeeded. 0 Error(s), 0 Warning(s)`

- [ ] **Step 6: Commit**

```bash
git add src/Geef.Atelier.Core/Domain/RunEntity.cs \
        src/Geef.Atelier.Infrastructure/Persistence/Configurations/RunConfiguration.cs \
        src/Geef.Atelier.Infrastructure/Persistence/Migrations/20260523120000_Step23RunResume.cs \
        src/Geef.Atelier.Infrastructure/Persistence/Migrations/AtelierDbContextModelSnapshot.cs
git commit -m "feat(data): Step23RunResume — ParentRunId + SeedDraftText auf Runs"
```

---

## Task 2: Application Contracts

**Files:**
- Create: `src/Geef.Atelier.Application/Runs/ResumeOptions.cs`
- Modify: `src/Geef.Atelier.Core/Persistence/IRunPersistenceService.cs`
- Modify: `src/Geef.Atelier.Application/Runs/IRunService.cs`
- Modify: `tests/Geef.Atelier.Tests/Application/Runs/RunServiceUserIsolationTests.cs` (stub)
- Modify: `tests/Geef.Atelier.Tests/Application/Runs/RunServiceAttachmentTests.cs` (stub)

- [ ] **Step 1: Create ResumeOptions**

Create `src/Geef.Atelier.Application/Runs/ResumeOptions.cs`:

```csharp
namespace Geef.Atelier.Application.Runs;

/// <summary>Parameters for resuming a previously aborted or failed run.</summary>
public sealed record ResumeOptions(
    Guid ParentRunId,

    /// <summary>
    /// True: inject the last iteration's ArtifactText as seed draft.
    /// False: start fresh with the same briefing (clean retry).
    /// </summary>
    bool UseSeedDraft,

    /// <summary>
    /// When non-null, overrides the convergence policy's MaxIterations for the resumed run.
    /// </summary>
    int? MaxIterationsOverride
);
```

- [ ] **Step 2: Add CreateResumedRunAsync to IRunPersistenceService**

Open `src/Geef.Atelier.Core/Persistence/IRunPersistenceService.cs`. After `MarkRunFailedAsync` add:

```csharp
    /// <summary>
    /// Creates a new run that resumes a previously aborted or failed run.
    /// Sets <c>ParentRunId</c> and optionally <c>SeedDraftText</c>.
    /// </summary>
    Task<Guid> CreateResumedRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser,
        string? crewTemplateName,
        string? crewSnapshotJson,
        Guid parentRunId,
        string? seedDraftText,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 3: Add ResumeRunAsync to IRunService**

Open `src/Geef.Atelier.Application/Runs/IRunService.cs`. After `GetWelcomeStatsAsync` add:

```csharp
    /// <summary>
    /// Creates a new run that resumes a previously aborted or failed run.
    /// Returns the ID of the newly created run.
    /// Throws <see cref="InvalidOperationException"/> if the parent run does not exist,
    /// does not belong to <paramref name="requestingUsername"/> (when non-null),
    /// or is not in a resumable state (Aborted or Failed).
    /// </summary>
    Task<Guid> ResumeRunAsync(
        ResumeOptions options,
        string? requestingUsername,
        CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Update IRunPersistenceService stubs in existing test files**

In `tests/Geef.Atelier.Tests/Application/Runs/RunServiceUserIsolationTests.cs`, find `MinimalPersistenceService` and add:

```csharp
        public Task<Guid> CreateResumedRunAsync(string briefingText, string configJson,
            string? createdByUser, string? crewTemplateName, string? crewSnapshotJson,
            Guid parentRunId, string? seedDraftText, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());
```

In `tests/Geef.Atelier.Tests/Application/Runs/RunServiceAttachmentTests.cs`, find `CapturingPersistenceService` and add the same stub.

- [ ] **Step 5: Build to verify — 0 errors**

```bash
cd /srv/docker/websites/geef_atelier
dotnet build
```
Expected: `Build succeeded.` (RunService won't compile yet — fix by adding a stub implementation in RunService now: `public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default) => throw new NotImplementedException();`)

- [ ] **Step 6: Commit**

```bash
git add src/Geef.Atelier.Application/Runs/ResumeOptions.cs \
        src/Geef.Atelier.Core/Persistence/IRunPersistenceService.cs \
        src/Geef.Atelier.Application/Runs/IRunService.cs \
        src/Geef.Atelier.Application/Runs/RunService.cs \
        tests/Geef.Atelier.Tests/Application/Runs/RunServiceUserIsolationTests.cs \
        tests/Geef.Atelier.Tests/Application/Runs/RunServiceAttachmentTests.cs
git commit -m "feat(contracts): ResumeOptions + IRunService.ResumeRunAsync + IRunPersistenceService.CreateResumedRunAsync"
```

---

## Task 3: RunService.ResumeRunAsync + RunPersistenceService implementation

**Files:**
- Modify: `src/Geef.Atelier.Application/Runs/RunService.cs`
- Modify: `src/Geef.Atelier.Infrastructure/Persistence/RunPersistenceService.cs`
- Create: `tests/Geef.Atelier.Tests/Application/Runs/RunServiceResumeTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Geef.Atelier.Tests/Application/Runs/RunServiceResumeTests.cs`:

```csharp
using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Knowledge;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
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
        var parentId = Guid.NewGuid();
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

    // ── MaxIterationsOverride ─────────────────────────────────────────────

    [Fact]
    public async Task ResumeRunAsync_WithMaxIterationsOverride_PatchesConvergenceOverrideInSnapshot()
    {
        var parentId = Guid.NewGuid();
        var snapshot = new CrewSnapshot(
            SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
            TemplateName: null,
            Executor: SystemCrew.DefaultExecutorProfile,
            Reviewers: [SystemCrew.BriefingFidelityProfile],
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

    private static RunService MakeService(IRunPersistenceService persistence, IRunRepository repo) =>
        new RunService(
            persistence,
            repo,
            new NoOpCrewService(),
            new NoOpAdvisorConsultationRepository(),
            new NoOpKnowledgeService(),
            NullLogger<RunService>.Instance);

    private static RunEntity MakeRun(Guid id, RunStatus status, string? createdByUser) =>
        new RunEntity
        {
            Id = id, CreatedAt = DateTimeOffset.UtcNow, Status = status,
            BriefingText = "briefing", ConfigJson = "{}", CreatedByUser = createdByUser,
        };

    private static RunEntity MakeRunWithSnapshot(Guid id, RunStatus status, string? createdByUser, string? snapshotJson) =>
        new RunEntity
        {
            Id = id, CreatedAt = DateTimeOffset.UtcNow, Status = status,
            BriefingText = "briefing", ConfigJson = "{}", CreatedByUser = createdByUser,
            CrewSnapshot = snapshotJson,
        };

    private static IterationEntity MakeIteration(Guid runId, int number, string text) =>
        new IterationEntity
        {
            Id = Guid.NewGuid(), RunId = runId, IterationNumber = number,
            ArtifactText = text, CreatedAt = DateTimeOffset.UtcNow,
        };

    // ── Fakes ─────────────────────────────────────────────────────────────

    private sealed class CapturingPersistenceService : IRunPersistenceService
    {
        public Guid?   LastParentRunId   { get; private set; }
        public string? LastSeedDraftText { get; private set; }
        public string? LastSnapshotJson  { get; private set; }

        public Task<Guid> CreateRunAsync(string briefingText, string configJson,
            string? createdByUser = null, string? crewTemplateName = null,
            string? crewSnapshotJson = null, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<Guid> CreateResumedRunAsync(string briefingText, string configJson,
            string? createdByUser, string? crewTemplateName, string? crewSnapshotJson,
            Guid parentRunId, string? seedDraftText, CancellationToken cancellationToken = default)
        {
            LastParentRunId   = parentRunId;
            LastSeedDraftText = seedDraftText;
            LastSnapshotJson  = crewSnapshotJson;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task UpdateSnapshotAsync(Guid runId, string snapshotJson, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkRunFailedAsync(Guid runId, string errorMessage, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class SingleDetailsRepository(RunEntity run, IReadOnlyList<IterationEntity> iterations) : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(runId == run.Id ? run : (RunEntity?)null);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(runId == run.Id ? new RunDetails(run, iterations) : (RunDetails?)null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([run]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken ct = default)
            => Task.FromResult(false);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken ct = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
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

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? username, CancellationToken ct = default)
            => Task.FromResult(new WelcomeStats(0, 0.0, 0.0, 0m, 0, 0m));
    }

    private sealed class NoOpCrewService : ICrewService
    {
        public Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default)
        {
            var s = new CrewSnapshot(CrewSnapshot.CurrentSchemaVersion, null, SystemCrew.DefaultExecutorProfile,
                [SystemCrew.BriefingFidelityProfile], EvaluationStrategy.Parallel, null, []);
            return Task.FromResult(s);
        }
        public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
        public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ReviewerProfile?>(null);
        public Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomReviewerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
        public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<ExecutorProfile?>(null);
        public Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomExecutorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
        public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<AdvisorProfile?>(null);
        public Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomAdvisorProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
        public Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<GroundingProviderProfile?>(null);
        public Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomGroundingProviderProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
        public Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.FromResult<FinalizerProfile?>(null);
        public Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default) => Task.FromResult(p);
        public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomFinalizerProfileAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
        public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
        public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default) => Task.FromResult<CrewTemplate?>(null);
        public Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default) => Task.FromResult(t);
        public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default) => Task.CompletedTask;
        public Task<string> RenameCustomCrewTemplateAsync(string o, string n, CancellationToken ct = default) => Task.FromResult(n);
    }

    private sealed class NoOpAdvisorConsultationRepository : IAdvisorConsultationRepository
    {
        public Task<AdvisorConsultation> CreateAsync(AdvisorConsultation c, CancellationToken ct) => Task.FromResult(c);
        public Task<IReadOnlyList<AdvisorConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct) => Task.FromResult<IReadOnlyList<AdvisorConsultation>>([]);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail (compilation error expected)**

```bash
cd /srv/docker/websites/geef_atelier
dotnet test --filter "FullyQualifiedName~RunServiceResumeTests" 2>&1 | head -30
```
Expected: build error — `RunService does not implement member 'IRunService.ResumeRunAsync'` (or similar).

- [ ] **Step 3: Implement CreateResumedRunAsync in RunPersistenceService**

Open `src/Geef.Atelier.Infrastructure/Persistence/RunPersistenceService.cs`. Add after `MarkRunFailedAsync`:

```csharp
    /// <inheritdoc/>
    public async Task<Guid> CreateResumedRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser,
        string? crewTemplateName,
        string? crewSnapshotJson,
        Guid parentRunId,
        string? seedDraftText,
        CancellationToken cancellationToken = default)
    {
        var run = new RunEntity
        {
            Id               = Guid.NewGuid(),
            CreatedAt        = DateTimeOffset.UtcNow,
            Status           = RunStatus.Pending,
            BriefingText     = briefingText,
            ConfigJson       = configJson,
            CreatedByUser    = createdByUser,
            CrewTemplateName = crewTemplateName,
            CrewSnapshot     = crewSnapshotJson,
            TokensTotal      = 0,
            CostTotal        = 0m,
            ParentRunId      = parentRunId,
            SeedDraftText    = seedDraftText,
        };

        db.Runs.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run.Id;
    }
```

- [ ] **Step 4: Implement ResumeRunAsync in RunService**

Open `src/Geef.Atelier.Application/Runs/RunService.cs`. Remove the `throw new NotImplementedException()` stub and add the real implementation. The `RunService` already has `SnapshotJsonOpts` as a static field. Add the method:

```csharp
    /// <inheritdoc/>
    public async Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var details = await repository.GetDetailsAsync(options.ParentRunId, cancellationToken);
        if (details is null)
            throw new InvalidOperationException($"Run {options.ParentRunId} not found.");

        if (requestingUsername is not null && details.Run.CreatedByUser != requestingUsername)
            throw new InvalidOperationException($"Run {options.ParentRunId} does not belong to user {requestingUsername}.");

        if (details.Run.Status is not (RunStatus.Aborted or RunStatus.Failed))
            throw new InvalidOperationException(
                $"Run {options.ParentRunId} is in status {details.Run.Status} and cannot be resumed. Only Aborted and Failed runs are resumable.");

        string? seedDraftText = null;
        if (options.UseSeedDraft && details.Iterations.Count > 0)
        {
            seedDraftText = details.Iterations
                .OrderByDescending(i => i.IterationNumber)
                .First()
                .ArtifactText;
        }

        var snapshotJson = details.Run.CrewSnapshot;
        if (options.MaxIterationsOverride.HasValue && !string.IsNullOrEmpty(snapshotJson))
        {
            var snapshot = CrewSnapshot.Deserialize(snapshotJson);
            if (snapshot is not null)
            {
                var patched = snapshot with
                {
                    ConvergenceOverride = (snapshot.ConvergenceOverride ?? new ConvergencePolicyOverride(null, null, null, null))
                        with { MaxIterations = options.MaxIterationsOverride }
                };
                snapshotJson = JsonSerializer.Serialize(patched, SnapshotJsonOpts);
            }
        }

        return await persistence.CreateResumedRunAsync(
            details.Run.BriefingText,
            details.Run.ConfigJson,
            details.Run.CreatedByUser,
            details.Run.CrewTemplateName,
            snapshotJson,
            options.ParentRunId,
            seedDraftText,
            cancellationToken);
    }
```

Add the required `using` at the top of `RunService.cs` (if not already present):
```csharp
using Geef.Atelier.Core.Domain.Crew;
```

- [ ] **Step 5: Run tests — expect pass**

```bash
cd /srv/docker/websites/geef_atelier
dotnet test --filter "FullyQualifiedName~RunServiceResumeTests" -v
```
Expected: all 9 tests pass.

- [ ] **Step 6: Run all tests to verify no regressions**

```bash
cd /srv/docker/websites/geef_atelier
dotnet test
```
Expected: all tests pass (previous count + 9 new).

- [ ] **Step 7: Commit**

```bash
git add src/Geef.Atelier.Application/Runs/RunService.cs \
        src/Geef.Atelier.Infrastructure/Persistence/RunPersistenceService.cs \
        tests/Geef.Atelier.Tests/Application/Runs/RunServiceResumeTests.cs
git commit -m "feat(service): RunService.ResumeRunAsync + RunPersistenceService.CreateResumedRunAsync"
```

---

## Task 4: Pipeline Layer — SeedDraftGroundingStep + ProfileBasedExecutor + Factory

**Files:**
- Modify: `src/Geef.Atelier.Infrastructure/Pipeline/AtelierContextKeys.cs`
- Create: `src/Geef.Atelier.Infrastructure/Pipeline/SeedDraftGroundingStep.cs`
- Modify: `src/Geef.Atelier.Infrastructure/Pipeline/ProfileBasedExecutor.cs`
- Modify: `src/Geef.Atelier.Infrastructure/Pipeline/AtelierPipelineFactory.cs`
- Create: `tests/Geef.Atelier.Tests/Infrastructure/Pipeline/SeedDraftGroundingStepTests.cs`
- Create: `tests/Geef.Atelier.Tests/Infrastructure/Pipeline/ProfileBasedExecutorSeedDraftTests.cs`

- [ ] **Step 1: Write failing tests for SeedDraftGroundingStep**

Create `tests/Geef.Atelier.Tests/Infrastructure/Pipeline/SeedDraftGroundingStepTests.cs`:

```csharp
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk.Context;

namespace Geef.Atelier.Tests.Infrastructure.Pipeline;

public sealed class SeedDraftGroundingStepTests
{
    [Fact]
    public async Task RunAsync_SetsGroundedBriefFromInput()
    {
        var step   = new SeedDraftGroundingStep("previous draft");
        var result = await step.RunAsync("the briefing", CancellationToken.None);

        var brief = result.Context.GetRequired(AtelierContextKeys.GroundedBrief);
        Assert.Equal("the briefing", brief);
    }

    [Fact]
    public async Task RunAsync_SetsSeedDraftFromConstructorArg()
    {
        var step   = new SeedDraftGroundingStep("my seed draft text");
        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.True(result.Context.TryGet(AtelierContextKeys.SeedDraft, out var seedDraft));
        Assert.Equal("my seed draft text", seedDraft);
    }

    [Fact]
    public async Task RunAsync_ReturnsEmptyNotes()
    {
        var step   = new SeedDraftGroundingStep("draft");
        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.Empty(result.Notes);
    }
}
```

- [ ] **Step 2: Add SeedDraft context key**

Open `src/Geef.Atelier.Infrastructure/Pipeline/AtelierContextKeys.cs`. After the `GroundingContext` key add:

```csharp
    /// <summary>
    /// Injected by <c>SeedDraftGroundingStep</c> for resume runs. Contains the artifact text of the
    /// last completed iteration of the parent run. <c>ProfileBasedExecutor</c> uses it on iteration 1
    /// to prime the LLM with a prior draft rather than generating from scratch.
    /// </summary>
    public static readonly ContextKey<string>         SeedDraft        = new("geef:atelier:seed-draft");
```

- [ ] **Step 3: Create SeedDraftGroundingStep**

Create `src/Geef.Atelier.Infrastructure/Pipeline/SeedDraftGroundingStep.cs`:

```csharp
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Grounding step for resume runs. Sets the grounded brief and injects the seed draft
/// (last iteration's ArtifactText) into the run context so ProfileBasedExecutor can
/// use it on iteration 1 instead of generating from scratch.
/// </summary>
internal sealed class SeedDraftGroundingStep(string seedDraftText) : IGroundingStep
{
    public Task<GroundingResult> RunAsync(string input, CancellationToken cancellationToken)
    {
        var context = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, input)
            .Set(AtelierContextKeys.SeedDraft, seedDraftText);

        return Task.FromResult(new GroundingResult { Context = context, Notes = [] });
    }
}
```

- [ ] **Step 4: Run SeedDraftGroundingStep tests — expect pass**

```bash
cd /srv/docker/websites/geef_atelier
dotnet test --filter "FullyQualifiedName~SeedDraftGroundingStepTests" -v
```
Expected: 3 tests pass.

- [ ] **Step 5: Write failing tests for ProfileBasedExecutor seed-draft branch**

Create `tests/Geef.Atelier.Tests/Infrastructure/Pipeline/ProfileBasedExecutorSeedDraftTests.cs`:

```csharp
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk;
using Geef.Sdk.Context;

namespace Geef.Atelier.Tests.Infrastructure.Pipeline;

public sealed class ProfileBasedExecutorSeedDraftTests
{
    [Fact]
    public async Task RunAsync_Iteration1_WithSeedDraft_IncludesPreviousDraftInPrompt()
    {
        var captured  = new CapturingLlmClient();
        var executor  = MakeExecutor(captured);
        var context   = MakeContext(iter: 1, seedDraft: "original draft text");

        await executor.RunAsync(context, CancellationToken.None);

        Assert.Contains("original draft text", captured.LastRequest!.UserPrompt);
        Assert.Contains("interrupted run", captured.LastRequest.UserPrompt);
    }

    [Fact]
    public async Task RunAsync_Iteration1_WithoutSeedDraft_UsesNormalWritePrompt()
    {
        var captured = new CapturingLlmClient();
        var executor = MakeExecutor(captured);
        var context  = MakeContext(iter: 1, seedDraft: null);

        await executor.RunAsync(context, CancellationToken.None);

        Assert.DoesNotContain("interrupted run", captured.LastRequest!.UserPrompt);
        Assert.Contains("Write a text", captured.LastRequest.UserPrompt);
    }

    [Fact]
    public async Task RunAsync_Iteration2_WithSeedDraftInContext_IgnoresSeedDraft()
    {
        var captured = new CapturingLlmClient("iter-2-output");
        var executor = MakeExecutor(captured);
        // On iter 2 the seed draft is still in context — should not affect prompt.
        var context  = MakeContext(iter: 2, seedDraft: "seed from resume", currentDraft: "iter 1 output");

        await executor.RunAsync(context, CancellationToken.None);

        Assert.DoesNotContain("interrupted run", captured.LastRequest!.UserPrompt);
        Assert.DoesNotContain("seed from resume", captured.LastRequest.UserPrompt);
        Assert.Contains("iter 1 output", captured.LastRequest.UserPrompt);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ProfileBasedExecutor MakeExecutor(ILlmClient client)
    {
        var profile = new ExecutorProfile(
            Name: "test", DisplayName: "Test", IsSystem: false,
            Provider: "test-provider", Model: "test-model",
            SystemPrompt: "You are a writer.", MaxTokens: 1000);

        return new ProfileBasedExecutor(profile, new StubResolver(client));
    }

    private static IRunContext MakeContext(int iter, string? seedDraft, string? currentDraft = null)
    {
        var ctx = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, "the briefing")
            .Set(GeefKeys.CurrentIteration, iter);

        if (seedDraft is not null)
            ctx = ctx.Set(AtelierContextKeys.SeedDraft, seedDraft);

        if (currentDraft is not null)
            ctx = ctx.Set(AtelierContextKeys.CurrentDraft, currentDraft);

        return ctx;
    }

    // ── Fakes ─────────────────────────────────────────────────────────────

    private sealed class CapturingLlmClient(string responseText = "generated text") : ILlmClient
    {
        public LlmRequest? LastRequest { get; private set; }

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new LlmResponse
            {
                Text         = responseText,
                TokenUsage   = new LlmTokenUsage { InputTokens = 10, OutputTokens = 5 },
                FinishReason = "stop"
            });
        }
    }

    private sealed class StubResolver(ILlmClient client) : ILlmClientResolver
    {
        public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName)
            => (client, "test-model", 1000);

        public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
            => (client, model, maxTokens ?? 1000);
    }
}
```

- [ ] **Step 6: Run ProfileBasedExecutor tests — expect fail**

```bash
cd /srv/docker/websites/geef_atelier
dotnet test --filter "FullyQualifiedName~ProfileBasedExecutorSeedDraftTests" -v
```
Expected: tests for seed-draft branch fail (prompt does not contain "interrupted run").

- [ ] **Step 7: Modify ProfileBasedExecutor to handle SeedDraft on iteration 1**

Open `src/Geef.Atelier.Infrastructure/Pipeline/ProfileBasedExecutor.cs`. Replace the `if (iter == 1)` block:

```csharp
        string userPrompt;
        if (iter == 1)
        {
            if (context.TryGet(AtelierContextKeys.SeedDraft, out var seedDraft) && seedDraft is not null)
            {
                userPrompt = $"""
                    Briefing:
                    {brief}

                    Previous draft (from an interrupted run — revise and improve it):
                    {seedDraft}

                    Revise the draft to better fulfill the briefing. Improve quality, address any
                    weaknesses you can identify, and make the text more polished.
                    """;
            }
            else
            {
                userPrompt = $"Briefing:\n{brief}\n\nWrite a text according to the briefing.";
            }
        }
        else
        {
```

- [ ] **Step 8: Run ProfileBasedExecutor tests — expect pass**

```bash
cd /srv/docker/websites/geef_atelier
dotnet test --filter "FullyQualifiedName~ProfileBasedExecutorSeedDraftTests" -v
```
Expected: all 3 tests pass.

- [ ] **Step 9: Add BuildWithSeedDraft to AtelierPipelineFactory**

Open `src/Geef.Atelier.Infrastructure/Pipeline/AtelierPipelineFactory.cs`. Add the new overload after `BuildWithAdvisorContext` (before `BuildWithProviders`):

```csharp
    /// <summary>
    /// Builds the pipeline like <see cref="Build"/>, but also injects the last completed iteration's
    /// artifact text as a seed draft into the initial run context. Used by the orchestrator for resume
    /// runs where <paramref name="seedDraftText"/> is non-null.
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> BuildWithSeedDraft(
        CrewSnapshot snapshot,
        ILlmClientResolver resolver,
        IOptions<ConvergenceOptions> convergenceOptions,
        string seedDraftText,
        IAdvisorConsultationRepository? consultationRepository = null,
        Guid runId = default,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null,
        IGroundingProviderFactory? groundingProviderFactory = null,
        IPricingCatalog? pricingCatalog = null,
        ICostAccumulator? costAccumulator = null)
    {
        IGroundingStep grounding = new SeedDraftGroundingStep(seedDraftText);
        if (snapshot.GroundingProviders is { Count: > 0 } && groundingProviderFactory is not null)
        {
            grounding = new MultiProviderGroundingStep(
                grounding, snapshot.GroundingProviders, groundingProviderFactory, runId,
                loggerFactory?.CreateLogger<MultiProviderGroundingStep>()
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MultiProviderGroundingStep>.Instance);
        }

        IExecutionStep execution = new ProfileBasedExecutor(snapshot.Executor, resolver, pricingCatalog, costAccumulator);

        var preExecutionAdvisors = snapshot.Advisors
            .Where(a => a.Trigger != AdvisorTrigger.OnConvergenceFailure)
            .ToList();

        if (preExecutionAdvisors.Count > 0 && consultationRepository is not null)
        {
            var advisorInstances = preExecutionAdvisors
                .Select(a => new ProfileBasedAdvisor(a, resolver, consultationRepository, pricingCatalog, costAccumulator))
                .ToList();
            execution = new AdvisorAwareExecutor(execution, advisorInstances, runId);
        }

        var reviewers = snapshot.Reviewers
            .Select(r => (IReviewer)new ProfileBasedReviewer(r, resolver, pricingCatalog, costAccumulator));
        var finalizer = new MarkdownFinalizer();

        return BuildWithProviders(grounding, execution, reviewers, finalizer,
            convergenceOptions, snapshot.ConvergenceOverride,
            snapshot.EvaluationStrategy, loggerFactory, additionalSinks);
    }
```

- [ ] **Step 10: Build + run all tests**

```bash
cd /srv/docker/websites/geef_atelier
dotnet build && dotnet test
```
Expected: 0 errors, all tests pass.

- [ ] **Step 11: Commit**

```bash
git add src/Geef.Atelier.Infrastructure/Pipeline/AtelierContextKeys.cs \
        src/Geef.Atelier.Infrastructure/Pipeline/SeedDraftGroundingStep.cs \
        src/Geef.Atelier.Infrastructure/Pipeline/ProfileBasedExecutor.cs \
        src/Geef.Atelier.Infrastructure/Pipeline/AtelierPipelineFactory.cs \
        tests/Geef.Atelier.Tests/Infrastructure/Pipeline/SeedDraftGroundingStepTests.cs \
        tests/Geef.Atelier.Tests/Infrastructure/Pipeline/ProfileBasedExecutorSeedDraftTests.cs
git commit -m "feat(pipeline): SeedDraftGroundingStep + BuildWithSeedDraft + iter-1 seed-draft prompt"
```

---

## Task 5: Orchestrator Wiring

**Files:**
- Modify: `src/Geef.Atelier.Web/Services/RunOrchestratorService.cs`

- [ ] **Step 1: Replace Build() dispatch with SeedDraftText check**

Open `src/Geef.Atelier.Web/Services/RunOrchestratorService.cs`. In `ProcessRunAsync`, find:

```csharp
            var runner = AtelierPipelineFactory.Build(
                snapshot, llmClientResolver, convergenceOptions,
                consultationRepository: consultations,
                runId: run.Id,
                loggerFactory: loggerFactory,
                additionalSinks: [sink],
                groundingProviderFactory: groundingProviderFactory,
                pricingCatalog: pricingCatalog,
                costAccumulator: accumulator);
```

Replace with:

```csharp
            var runner = run.SeedDraftText is not null
                ? AtelierPipelineFactory.BuildWithSeedDraft(
                    snapshot, llmClientResolver, convergenceOptions, run.SeedDraftText,
                    consultationRepository: consultations,
                    runId: run.Id,
                    loggerFactory: loggerFactory,
                    additionalSinks: [sink],
                    groundingProviderFactory: groundingProviderFactory,
                    pricingCatalog: pricingCatalog,
                    costAccumulator: accumulator)
                : AtelierPipelineFactory.Build(
                    snapshot, llmClientResolver, convergenceOptions,
                    consultationRepository: consultations,
                    runId: run.Id,
                    loggerFactory: loggerFactory,
                    additionalSinks: [sink],
                    groundingProviderFactory: groundingProviderFactory,
                    pricingCatalog: pricingCatalog,
                    costAccumulator: accumulator);
```

- [ ] **Step 2: Build + run all tests**

```bash
cd /srv/docker/websites/geef_atelier
dotnet build && dotnet test
```
Expected: 0 errors, all tests pass.

- [ ] **Step 3: Commit**

```bash
git add src/Geef.Atelier.Web/Services/RunOrchestratorService.cs
git commit -m "feat(orchestrator): dispatch BuildWithSeedDraft für Resume-Runs"
```

---

## Task 6: UI — ResumeRunDialog Component

**Files:**
- Create: `src/Geef.Atelier.Web/Components/UI/ResumeRunDialog.razor`
- Create: `tests/Geef.Atelier.Tests/Web/Components/ResumeRunDialogTests.cs`

- [ ] **Step 1: Write failing bUnit tests**

Create `tests/Geef.Atelier.Tests/Web/Components/ResumeRunDialogTests.cs`:

```csharp
using Bunit;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

public sealed class ResumeRunDialogTests : TestContext
{
    [Fact]
    public void Show_False_DialogNotRendered()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, false);
            p.Add(c => c.DefaultMaxIterations, 3);
        });

        Assert.Empty(cut.Markup.Trim());
    }

    [Fact]
    public void Show_True_DialogVisible()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DefaultMaxIterations, 3);
        });

        cut.Find("[data-testid='resume-dialog']");
    }

    [Fact]
    public void Show_True_SeedModeSelectedByDefault_WhenHasIterations()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.HasIterations, true);
            p.Add(c => c.DefaultMaxIterations, 3);
        });

        var seedRadio = cut.Find("[data-testid='resume-mode-seed']");
        Assert.True(seedRadio.IsChecked());
    }

    [Fact]
    public void Show_True_CleanModeSelectedByDefault_WhenNoIterations()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.HasIterations, false);
            p.Add(c => c.DefaultMaxIterations, 3);
        });

        var cleanRadio = cut.Find("[data-testid='resume-mode-clean']");
        Assert.True(cleanRadio.IsChecked());
    }

    [Fact]
    public void MaxIterations_PrefilledWithDefaultValue()
    {
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DefaultMaxIterations, 7);
        });

        var input = cut.Find("[data-testid='resume-max-iterations']");
        Assert.Equal("7", input.GetAttribute("value"));
    }

    [Fact]
    public async Task Confirm_SeedMode_InvokesOnConfirmWithUseSeedDraftTrue()
    {
        ResumeOptions? captured = null;
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.HasIterations, true);
            p.Add(c => c.DefaultMaxIterations, 3);
            p.Add(c => c.OnConfirm, EventCallback.Factory.Create<ResumeOptions>(this, opts => captured = opts));
        });

        await cut.Find("[data-testid='resume-confirm-button']").ClickAsync(new());

        Assert.NotNull(captured);
        Assert.True(captured!.UseSeedDraft);
    }

    [Fact]
    public async Task Confirm_CleanMode_InvokesOnConfirmWithUseSeedDraftFalse()
    {
        ResumeOptions? captured = null;
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.HasIterations, true);
            p.Add(c => c.DefaultMaxIterations, 3);
            p.Add(c => c.OnConfirm, EventCallback.Factory.Create<ResumeOptions>(this, opts => captured = opts));
        });

        // Switch to clean mode
        cut.Find("[data-testid='resume-mode-clean']").Change(true);
        await cut.Find("[data-testid='resume-confirm-button']").ClickAsync(new());

        Assert.NotNull(captured);
        Assert.False(captured!.UseSeedDraft);
    }

    [Fact]
    public async Task Confirm_SendsMaxIterationsOverride()
    {
        ResumeOptions? captured = null;
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DefaultMaxIterations, 3);
            p.Add(c => c.OnConfirm, EventCallback.Factory.Create<ResumeOptions>(this, opts => captured = opts));
        });

        cut.Find("[data-testid='resume-max-iterations']").Change("10");
        await cut.Find("[data-testid='resume-confirm-button']").ClickAsync(new());

        Assert.Equal(10, captured!.MaxIterationsOverride);
    }

    [Fact]
    public async Task Cancel_InvokesOnCancel()
    {
        var cancelled = false;
        var cut = RenderComponent<ResumeRunDialog>(p =>
        {
            p.Add(c => c.Show, true);
            p.Add(c => c.DefaultMaxIterations, 3);
            p.Add(c => c.OnCancel, EventCallback.Factory.Create(this, () => cancelled = true));
        });

        await cut.Find("[data-testid='resume-cancel-button']").ClickAsync(new());

        Assert.True(cancelled);
    }
}
```

- [ ] **Step 2: Run tests — expect fail (component doesn't exist)**

```bash
cd /srv/docker/websites/geef_atelier
dotnet test --filter "FullyQualifiedName~ResumeRunDialogTests" 2>&1 | head -20
```
Expected: compilation error — `ResumeRunDialog` not found.

- [ ] **Step 3: Create ResumeRunDialog.razor**

Create `src/Geef.Atelier.Web/Components/UI/ResumeRunDialog.razor`:

```razor
@using Geef.Atelier.Application.Runs
@using Geef.Atelier.Web.Components.UI

@if (Show)
{
    <Modal Show="Show" OnClose="OnCancel" CloseOnBackdropClick="true" DataTestId="resume-dialog">
        <Title>
            <span>Run fortsetzen</span>
        </Title>
        <ChildContent>
            <div class="resume-mode-selector">
                <label class="resume-mode-option">
                    <input type="radio"
                           data-testid="resume-mode-seed"
                           name="resume-mode"
                           checked="@_useSeedDraft"
                           @onchange="() => _useSeedDraft = true"
                           disabled="@(!HasIterations)" />
                    <span>
                        <strong>Mit letztem Entwurf fortsetzen</strong>
                        <span class="field-hint">Die KI bekommt den letzten Entwurf als Ausgangspunkt.</span>
                        @if (!HasIterations)
                        {
                            <span class="field-hint t-muted">(Kein Entwurf verfügbar)</span>
                        }
                    </span>
                </label>
                <label class="resume-mode-option">
                    <input type="radio"
                           data-testid="resume-mode-clean"
                           name="resume-mode"
                           checked="@(!_useSeedDraft)"
                           @onchange="() => _useSeedDraft = false" />
                    <span>
                        <strong>Komplett neu starten</strong>
                        <span class="field-hint">Selbes Briefing, keine Seedvorlage.</span>
                    </span>
                </label>
            </div>

            <div class="form-field">
                <label>Max. Iterationen</label>
                <input type="number"
                       data-testid="resume-max-iterations"
                       class="input-narrow"
                       min="1"
                       max="30"
                       value="@_maxIterations"
                       @onchange="e => int.TryParse(e.Value?.ToString(), out _maxIterations)" />
                <FieldHelp>Wie viele Iterationen die KI maximal durchführen soll.</FieldHelp>
            </div>
        </ChildContent>
        <Actions>
            <button type="button" class="btn primary"
                    data-testid="resume-confirm-button"
                    @onclick="HandleConfirm">
                Fortsetzen
            </button>
            <button type="button" class="btn ghost"
                    data-testid="resume-cancel-button"
                    @onclick="() => OnCancel.InvokeAsync()">
                Abbrechen
            </button>
        </Actions>
    </Modal>
}

@code {
    [Parameter] public bool Show { get; set; }
    [Parameter] public bool HasIterations { get; set; }
    [Parameter] public int DefaultMaxIterations { get; set; } = 3;
    [Parameter] public EventCallback<ResumeOptions> OnConfirm { get; set; }
    [Parameter] public EventCallback OnCancel { get; set; }

    // ParentRunId is set by the page before showing the dialog.
    [Parameter] public Guid ParentRunId { get; set; }

    private bool _useSeedDraft;
    private int  _maxIterations;

    protected override void OnParametersSet()
    {
        if (!Show) return;
        _useSeedDraft  = HasIterations;
        _maxIterations = DefaultMaxIterations;
    }

    private async Task HandleConfirm()
    {
        var opts = new ResumeOptions(
            ParentRunId:          ParentRunId,
            UseSeedDraft:         _useSeedDraft,
            MaxIterationsOverride: _maxIterations > 0 ? _maxIterations : null);
        await OnConfirm.InvokeAsync(opts);
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
cd /srv/docker/websites/geef_atelier
dotnet test --filter "FullyQualifiedName~ResumeRunDialogTests" -v
```
Expected: all 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Geef.Atelier.Web/Components/UI/ResumeRunDialog.razor \
        tests/Geef.Atelier.Tests/Web/Components/ResumeRunDialogTests.cs
git commit -m "feat(ui): ResumeRunDialog-Modal mit Seed/Clean-Modus und Max-Iterations"
```

---

## Task 7: UI — RunDetail Page Integration + Build + Deploy

**Files:**
- Modify: `src/Geef.Atelier.Web/Components/Pages/RunDetail.razor`

- [ ] **Step 1: Add Fortsetzen-Button and ResumeRunDialog to RunDetail.razor**

Open `src/Geef.Atelier.Web/Components/Pages/RunDetail.razor`. Find the `run-header` section where the existing Cancel-button or action buttons appear. Add:

a) In the `@using` directives block at the top, add:
```razor
@using Geef.Atelier.Application.Runs
```
(if not already present)

b) In the `run-header` action-buttons area, find where `<CancelButton ...>` is rendered or where status-specific actions live, and add after it (conditionally for Aborted/Failed):

```razor
@if (_details.Run.Status is RunStatus.Aborted or RunStatus.Failed)
{
    <button class="btn secondary" data-testid="resume-run-button"
            @onclick="OpenResumeDialog">
        Fortsetzen
    </button>
}
```

c) Add the ParentRunId link in the run-header meta area (after the CreatedAt or similar meta line):

```razor
@if (_details.Run.ParentRunId.HasValue)
{
    <div class="parent-run-link">
        Fortgesetzt von
        <a href="/runs/@_details.Run.ParentRunId.Value" class="t-mono">
            @_details.Run.ParentRunId.Value.ToString()[..8]
        </a>
    </div>
}
```

d) Add the ResumeRunDialog below the main content (before the closing `</div>` of the outer `detail` div):

```razor
<ResumeRunDialog
    Show="@_showResumeDialog"
    HasIterations="@(_details?.Iterations.Count > 0)"
    DefaultMaxIterations="@GetEffectiveMaxIterations()"
    ParentRunId="@RunId"
    OnConfirm="HandleResumeConfirmedAsync"
    OnCancel="() => _showResumeDialog = false" />
```

e) In the `@code` block, add the dialog state and handlers:

```csharp
    private bool _showResumeDialog = false;

    private void OpenResumeDialog() => _showResumeDialog = true;

    private int GetEffectiveMaxIterations()
    {
        if (_details?.Run.CrewSnapshot is { } json)
        {
            var snapshot = CrewSnapshot.Deserialize(json);
            if (snapshot?.ConvergenceOverride?.MaxIterations is { } max)
                return max;
        }
        return 3; // fallback to system default display
    }

    private async Task HandleResumeConfirmedAsync(ResumeOptions opts)
    {
        _showResumeDialog = false;
        try
        {
            var newRunId = await RunService.ResumeRunAsync(opts, _requestingUser);
            Nav.NavigateTo($"/runs/{newRunId}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Resume failed for run {RunId}", RunId);
        }
    }
```

`_requestingUser` is already set in `OnInitializedAsync` (null for admins, username otherwise) — pattern is identical to `HandleCancelledAsync` and `HandlePromoteAsync` in the same file.

- [ ] **Step 2: Build to verify — 0 errors**

```bash
cd /srv/docker/websites/geef_atelier
dotnet build
```
Expected: `Build succeeded. 0 Error(s), 0 Warning(s)`

- [ ] **Step 3: Run all tests**

```bash
cd /srv/docker/websites/geef_atelier
dotnet test
```
Expected: all tests pass (previous count + ~24 new).

- [ ] **Step 4: Commit**

```bash
git add src/Geef.Atelier.Web/Components/Pages/RunDetail.razor
git commit -m "feat(ui): Fortsetzen-Button + ParentRunId-Link in RunDetail"
```

- [ ] **Step 5: Deploy to production**

```bash
# Backup first
mkdir -p /srv/backup
docker exec geef-atelier-postgres pg_dump -U geef_atelier -d geef_atelier \
  --format=custom --compress=9 \
  > /srv/backup/before-run-resume-$(date +%Y%m%d-%H%M%S).dump

# Build and deploy
cd /srv/docker/websites/geef_atelier
docker compose build --no-cache web
docker compose up -d web

# Wait for startup
sleep 15

# Verify migration ran
docker exec geef-atelier-postgres psql -U geef_atelier -d geef_atelier -c \
  "SELECT column_name FROM information_schema.columns \
   WHERE table_name='Runs' AND column_name IN ('ParentRunId','SeedDraftText');"
# Expected: 2 rows

# Health check
curl -I https://geef.stefan-bechtel.de/
# Expected: 200 or 302
```

- [ ] **Step 6: Smoke test in browser**

Open `https://geef.stefan-bechtel.de/runs` and find a run with status `Aborted` or `Failed`.

Verify:
- [ ] "Fortsetzen"-Button is visible on the run's detail page
- [ ] Clicking "Fortsetzen" opens `ResumeRunDialog`
- [ ] Dialog shows both mode radios and max-iterations field
- [ ] Confirming creates a new run and redirects to `/runs/{newRunId}`
- [ ] New run's detail page shows "Fortgesetzt von [parent-id]" link
- [ ] Parent run link navigates back correctly
- [ ] Runs without Aborted/Failed status do NOT show the button
