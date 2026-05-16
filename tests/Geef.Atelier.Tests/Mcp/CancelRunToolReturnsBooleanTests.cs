using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

public sealed class CancelRunToolReturnsBooleanTests
{
    [Fact]
    public async Task CancelRun_WithValidRunId_ReturnsTrue()
    {
        var runId = Guid.NewGuid();
        var fakeService = new FakeRunServiceCancellable(runId);
        var result = await CancelRunTool.CancelRun(
            fakeService,
            runId: runId.ToString(),
            cancellationToken: default);

        Assert.True(result);
    }

    [Fact]
    public async Task CancelRun_WithInvalidRunId_ReturnsFalse()
    {
        var fakeService = new FakeRunServiceCancellable(Guid.NewGuid());
        var result = await CancelRunTool.CancelRun(
            fakeService,
            runId: "not-a-guid",
            cancellationToken: default);

        Assert.False(result);
    }

    private sealed class FakeRunServiceCancellable(Guid cancellableId) : IRunService
    {
        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(Guid.NewGuid());

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);

        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>(Array.Empty<RunEntity>());

        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(runId == cancellableId);

        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunDetails?>(null);

        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunWithGroundingViewModel?>(null);

        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult(new WelcomeStats(0, 0, 0, 0, 0, 0));
    }
}
