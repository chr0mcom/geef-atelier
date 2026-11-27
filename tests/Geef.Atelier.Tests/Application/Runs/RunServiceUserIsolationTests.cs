using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Runs;

/// <summary>
/// Unit tests for RunService ownership-check logic.
/// Verifies that non-null requestingUsername triggers isolation,
/// and null requestingUsername bypasses ownership checks (admin mode).
/// </summary>
public sealed class RunServiceUserIsolationTests
{
    // ---------------------------------------------------------------------------
    // GetRunAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRunAsync_WithMatchingUsername_ReturnsRun()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: false);
        var svc   = MakeService(repo);

        var result = await svc.GetRunAsync(runId, "alice");

        Assert.NotNull(result);
        Assert.Equal(runId, result!.Id);
    }

    [Fact]
    public async Task GetRunAsync_WithMismatchedUsername_ReturnsNull()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: false);
        var svc   = MakeService(repo);

        var result = await svc.GetRunAsync(runId, "bob");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRunAsync_WithNullUsername_ReturnsRunRegardlessOfOwner()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: false);
        var svc   = MakeService(repo);

        var result = await svc.GetRunAsync(runId, null);

        Assert.NotNull(result);
        Assert.Equal(runId, result!.Id);
    }

    // ---------------------------------------------------------------------------
    // ListRunsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListRunsAsync_PassesUsernameToRepository()
    {
        var repo = new RecordingRunRepository();
        var svc  = MakeService(repo);

        await svc.ListRunsAsync(10, null, "charlie");

        Assert.Equal("charlie", repo.LastListUsername);
    }

    [Fact]
    public async Task ListRunsAsync_PassesNullUsernameToRepository_ForAdminMode()
    {
        var repo = new RecordingRunRepository();
        var svc  = MakeService(repo);

        await svc.ListRunsAsync(10, null, null);

        Assert.Null(repo.LastListUsername);
    }

    // ---------------------------------------------------------------------------
    // CancelRunAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task CancelRunAsync_ByOwner_ReturnsTrueWhenCancellationSucceeds()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: true);
        var svc   = MakeService(repo);

        var result = await svc.CancelRunAsync(runId, "alice");

        Assert.True(result);
    }

    [Fact]
    public async Task CancelRunAsync_ByNonOwner_ReturnsFalse()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: true);
        var svc   = MakeService(repo);

        var result = await svc.CancelRunAsync(runId, "bob");

        Assert.False(result);
    }

    [Fact]
    public async Task CancelRunAsync_ByAdmin_BypassesOwnerCheck()
    {
        var runId = Guid.NewGuid();
        var run   = MakeRun(runId, "alice");
        var repo  = new SingleRunRepository(run, cancellable: true);
        var svc   = MakeService(repo);

        // null = admin mode, no ownership check
        var result = await svc.CancelRunAsync(runId, null);

        Assert.True(result);
    }

    // ---------------------------------------------------------------------------
    // GetRunDetailsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRunDetailsAsync_WithMatchingUsername_ReturnsDetails()
    {
        var runId   = Guid.NewGuid();
        var run     = MakeRun(runId, "alice");
        var details = new RunDetails(run, []);
        var repo    = new SingleRunRepository(run, cancellable: false, details: details);
        var svc     = MakeService(repo);

        var result = await svc.GetRunDetailsAsync(runId, "alice");

        Assert.NotNull(result);
        Assert.Equal(runId, result!.Run.Id);
    }

    [Fact]
    public async Task GetRunDetailsAsync_WithMismatchedUsername_ReturnsNull()
    {
        var runId   = Guid.NewGuid();
        var run     = MakeRun(runId, "alice");
        var details = new RunDetails(run, []);
        var repo    = new SingleRunRepository(run, cancellable: false, details: details);
        var svc     = MakeService(repo);

        var result = await svc.GetRunDetailsAsync(runId, "bob");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRunDetailsAsync_WithNullUsername_ReturnsAnyRun()
    {
        var runId   = Guid.NewGuid();
        var run     = MakeRun(runId, "alice");
        var details = new RunDetails(run, []);
        var repo    = new SingleRunRepository(run, cancellable: false, details: details);
        var svc     = MakeService(repo);

        var result = await svc.GetRunDetailsAsync(runId, null);

        Assert.NotNull(result);
        Assert.Equal(runId, result!.Run.Id);
    }

    // ---------------------------------------------------------------------------
    // Warnings of the run view
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task GetRunViewAsync_OwnerAndAdminSeeTheWarningsOfTheRun()
    {
        var runId    = Guid.NewGuid();
        var warnings = new InMemoryRunWarnings();
        await warnings.ReportAsync(runId, "grounding", "grounding.provider_skipped", "first");
        await warnings.ReportAsync(runId, "review", "review.reviewer_failed", "second");
        await warnings.ReportAsync(Guid.NewGuid(), "grounding", "grounding.provider_skipped", "other run");
        var svc = MakeService(RepositoryWithDetails(runId, "alice"), warnings);

        var forOwner = await svc.GetRunViewAsync(runId, "alice");
        var forAdmin = await svc.GetRunViewAsync(runId, requestingUsername: null);

        Assert.Equal(["first", "second"], forOwner!.Warnings.Select(w => w.Message).ToList());
        Assert.Equal(["first", "second"], forAdmin!.Warnings.Select(w => w.Message).ToList());
    }

    [Fact]
    public async Task GetRunViewAsync_OtherUserGetsNothing()
    {
        var runId    = Guid.NewGuid();
        var warnings = new InMemoryRunWarnings();
        await warnings.ReportAsync(runId, "grounding", "grounding.provider_skipped", "private");
        var svc = MakeService(RepositoryWithDetails(runId, "alice"), warnings);

        Assert.Null(await svc.GetRunViewAsync(runId, "bob"));
    }

    [Fact]
    public async Task GetRunViewAsync_UnknownRunGetsNothingForAUser()
    {
        var warnings = new InMemoryRunWarnings();
        var svc = MakeService(RepositoryWithDetails(Guid.NewGuid(), "alice"), warnings);

        Assert.Null(await svc.GetRunViewAsync(Guid.NewGuid(), "alice"));
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static RunService MakeService(IRunRepository repo, InMemoryRunWarnings? warnings = null)
    {
        warnings ??= new InMemoryRunWarnings();
        return new RunService(
            repo,
            NullLogger<RunService>.Instance,
            warnings,
            System.Reflection.DispatchProxy.Create<Geef.Atelier.Core.Persistence.Orders.IOrderRepository, UnusedOrders>());
    }

    private static SingleRunRepository RepositoryWithDetails(Guid id, string owner)
    {
        var run = MakeRun(id, owner);
        return new SingleRunRepository(run, cancellable: false, details: new RunDetails(run, []));
    }

    private static RunEntity MakeRun(Guid id, string? createdByUser) => new RunEntity
    {
        Id            = id,
        CreatedAt     = DateTimeOffset.UtcNow,
        Status        = RunStatus.Pending,
        BriefingText  = "briefing",
        CreatedByUser = createdByUser,
    };

    // ---------------------------------------------------------------------------
    // Fakes / stubs
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns a single pre-defined run (or its details) by ID.
    /// <paramref name="cancellable"/> controls whether <see cref="RequestCancellationAsync"/> returns true.
    /// </summary>
    private sealed class SingleRunRepository(RunEntity run, bool cancellable, RunDetails? details = null) : IRunRepository
    {
        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(runId == run.Id ? run : (RunEntity?)null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([run]);

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(runId == run.Id && cancellable);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(runId == run.Id ? details : null);

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Records the arguments passed to ListAsync so tests can inspect them.</summary>
    private sealed class RecordingRunRepository : IRunRepository
    {
        public string? LastListUsername { get; private set; } = "UNSET";

        public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);

        public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default)
        {
            LastListUsername = username;
            return Task.FromResult<IReadOnlyList<RunEntity>>([]);
        }

        public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult<RunDetails?>(null);

        public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public class UnusedOrders : System.Reflection.DispatchProxy
    {
        protected override object? Invoke(System.Reflection.MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException(targetMethod?.Name);
    }
}
