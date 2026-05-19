using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// Tests that ListRunsTool correctly maps the includeAllUsers flag and IsAdmin
/// to the requestingUsername passed to IRunService.
/// </summary>
public sealed class ListRunsToolUserFilterTests
{
    private static readonly FakeArtifactRepository _artifactRepo = new();

    [Fact]
    public async Task ListRuns_NonAdmin_AlwaysSeesOwnRuns()
    {
        var service     = new RecordingRunService();
        var currentUser = new FakeUser("alice", isAdmin: false);

        await ListRunsTool.ListRuns(service, currentUser, _artifactRepo, includeAllUsers: false);

        Assert.Equal("alice", service.LastRequestingUsername);
    }

    [Fact]
    public async Task ListRuns_Admin_WithIncludeAllFalse_SeesOwnRuns()
    {
        var service     = new RecordingRunService();
        var currentUser = new FakeUser("admin", isAdmin: true);

        await ListRunsTool.ListRuns(service, currentUser, _artifactRepo, includeAllUsers: false);

        Assert.Equal("admin", service.LastRequestingUsername);
    }

    [Fact]
    public async Task ListRuns_Admin_WithIncludeAllTrue_SeesAllRuns()
    {
        var service     = new RecordingRunService();
        var currentUser = new FakeUser("admin", isAdmin: true);

        await ListRunsTool.ListRuns(service, currentUser, _artifactRepo, includeAllUsers: true);

        // null = admin bypass: returns all users' runs
        Assert.Null(service.LastRequestingUsername);
    }

    [Fact]
    public async Task ListRuns_NonAdmin_WithIncludeAllTrue_StillSeesOwnRuns()
    {
        var service     = new RecordingRunService();
        var currentUser = new FakeUser("alice", isAdmin: false);

        // Non-admin cannot escalate privilege even if they pass includeAllUsers=true
        await ListRunsTool.ListRuns(service, currentUser, _artifactRepo, includeAllUsers: true);

        Assert.Equal("alice", service.LastRequestingUsername);
    }

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakeUser(string username, bool isAdmin) : ICurrentUserService
    {
        public string? Username    => username;
        public bool IsAuthenticated => true;
        public bool IsAdmin         => isAdmin;
    }

    private sealed class FakeArtifactRepository : IRunArtifactRepository
    {
        public Task<IReadOnlyList<RunArtifact>> ListByRunAsync(Guid runId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<RunArtifact>>([]);
        public Task<RunArtifact?> GetByIdAsync(Guid artifactId, CancellationToken ct)
            => Task.FromResult<RunArtifact?>(null);
        public Task<RunArtifact> CreateAsync(RunArtifact artifact, CancellationToken ct)
            => Task.FromResult(artifact);
        public Task DeleteByRunAsync(Guid runId, CancellationToken ct)
            => Task.CompletedTask;
    }

    /// <summary>Captures the requestingUsername argument passed to ListRunsAsync.</summary>
    private sealed class RecordingRunService : IRunService
    {
        public string? LastRequestingUsername { get; private set; } = "UNSET";

        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(
            int limit = 20,
            RunStatus? statusFilter = null,
            string? requestingUsername = null,
            CancellationToken cancellationToken = default)
        {
            LastRequestingUsername = requestingUsername;
            return Task.FromResult<IReadOnlyList<RunEntity>>([]);
        }

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);

        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunDetails?>(null);

        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunWithGroundingViewModel?>(null);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(new WelcomeStats(0, 0, 0, 0, 0, 0));

        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
