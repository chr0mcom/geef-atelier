using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

/// <summary>
/// Tests that GetRunDetailsTool passes ownership context correctly to IRunService,
/// and that the admin bypass (null requestingUsername) works as expected.
/// </summary>
public sealed class GetRunDetailsMcpOwnerCheckTests
{
    [Fact]
    public async Task GetRunDetails_AsOwner_ReturnsDetails()
    {
        var runId   = Guid.NewGuid();
        var details = MakeDetails(runId, "alice");
        var service = new OwnerFilteredRunService(details, ownerUsername: "alice");
        var user    = new FakeUser("alice", isAdmin: false);

        var result = await GetRunDetailsTool.GetRunDetails(service, user, runId.ToString());

        Assert.NotNull(result);
        Assert.Equal(runId.ToString(), result!.RunId);
    }

    [Fact]
    public async Task GetRunDetails_AsNonOwner_ReturnsNull()
    {
        var runId   = Guid.NewGuid();
        var details = MakeDetails(runId, "alice");
        var service = new OwnerFilteredRunService(details, ownerUsername: "alice");
        var user    = new FakeUser("bob", isAdmin: false);

        var result = await GetRunDetailsTool.GetRunDetails(service, user, runId.ToString());

        // Service receives "bob" as requestingUsername → mismatch → null
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRunDetails_AsAdmin_ReturnsAnyRun()
    {
        var runId   = Guid.NewGuid();
        var details = MakeDetails(runId, "alice");
        // Admin bypasses: service receives null → always returns details
        var service = new OwnerFilteredRunService(details, ownerUsername: "alice");
        var user    = new FakeUser("admin", isAdmin: true);

        var result = await GetRunDetailsTool.GetRunDetails(service, user, runId.ToString());

        Assert.NotNull(result);
        Assert.Equal(runId.ToString(), result!.RunId);
    }

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static RunDetails MakeDetails(Guid runId, string createdByUser) =>
        new RunDetails(
            new RunEntity
            {
                Id           = runId,
                CreatedAt    = DateTimeOffset.UtcNow,
                Status       = RunStatus.Completed,
                BriefingText = "briefing",
                ConfigJson   = "{}",
                CreatedByUser = createdByUser,
            },
            []);

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakeUser(string username, bool isAdmin) : ICurrentUserService
    {
        public string? Username     => username;
        public bool IsAuthenticated => true;
        public bool IsAdmin         => isAdmin;
    }

    /// <summary>
    /// Returns the stored <see cref="RunDetails"/> only when the requestingUsername
    /// matches the owner (simulating service-layer isolation), or when it is null
    /// (admin bypass).
    /// </summary>
    private sealed class OwnerFilteredRunService(RunDetails details, string ownerUsername) : IRunService
    {
        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
        {
            if (runId != details.Run.Id) return Task.FromResult<RunDetails?>(null);
            // null means admin bypass → always return
            if (requestingUsername is null || requestingUsername == ownerUsername)
                return Task.FromResult<RunDetails?>(details);
            return Task.FromResult<RunDetails?>(null);
        }

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);

        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>([]);

        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunWithGroundingViewModel?>(null);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(new WelcomeStats(0, 0, 0, 0, 0, 0));

        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
