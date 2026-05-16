using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

public sealed class GetRunStatusToolReturnsStatusTests
{
    private static readonly ICurrentUserService AdminUser = new FakeAdminUser();

    [Fact]
    public async Task GetRunStatus_WithValidRunId_ReturnsStatus()
    {
        var runId = Guid.NewGuid();
        var fakeService = new FakeRunServiceWithRun(runId, RunStatus.Running);
        var result = await GetRunStatusTool.GetRunStatus(
            fakeService,
            AdminUser,
            runId: runId.ToString(),
            cancellationToken: default);

        Assert.NotNull(result);
        Assert.Equal("Running", result!.Status);
        Assert.Equal(runId.ToString(), result.RunId);
    }

    [Fact]
    public async Task GetRunStatus_WithInvalidRunId_ReturnsNull()
    {
        var fakeService = new FakeRunServiceWithRun(Guid.NewGuid(), RunStatus.Pending);
        var result = await GetRunStatusTool.GetRunStatus(
            fakeService,
            AdminUser,
            runId: "not-a-guid",
            cancellationToken: default);

        Assert.Null(result);
    }

    private sealed class FakeAdminUser : ICurrentUserService
    {
        public string? Username => "admin";
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    private sealed class FakeRunServiceWithRun(Guid knownId, RunStatus status) : IRunService
    {
        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
        {
            if (runId != knownId) return Task.FromResult<RunEntity?>(null);
            var entity = new RunEntity
            {
                Id          = knownId,
                CreatedAt   = DateTimeOffset.UtcNow,
                Status      = status,
                BriefingText = "briefing",
                ConfigJson  = "{}",
            };
            return Task.FromResult<RunEntity?>(entity);
        }

        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>(Array.Empty<RunEntity>());

        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunDetails?>(null);

        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunWithGroundingViewModel?>(null);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(new WelcomeStats(0, 0, 0, 0, 0, 0));
    }
}
