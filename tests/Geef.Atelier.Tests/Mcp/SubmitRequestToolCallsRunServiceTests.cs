using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

public sealed class SubmitRequestToolCallsRunServiceTests
{
    private static readonly ICurrentUserService AdminUser = new FakeAdminUser();

    [Fact]
    public async Task SubmitRequest_CallsRunService_WithAuthClaimAsCreatedByUser()
    {
        var fakeService = new FakeRunService();
        var result = await SubmitRequestTool.SubmitRequest(
            fakeService,
            AdminUser,
            briefingText: "test briefing",
            configJson: null,
            cancellationToken: default);

        Assert.Equal("admin", fakeService.LastCreatedByUser);
        Assert.Equal("test briefing", fakeService.LastBriefingText);
        Assert.Equal("Pending", result.Status);
    }

    private sealed class FakeAdminUser : ICurrentUserService
    {
        public string? Username => "admin";
        public bool IsAuthenticated => true;
        public bool IsAdmin => true;
    }

    private sealed class FakeRunService : IRunService
    {
        public string? LastCreatedByUser { get; private set; }
        public string? LastBriefingText { get; private set; }

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
        {
            LastCreatedByUser = request.CreatedByUser;
            LastBriefingText = request.BriefingText;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);

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

        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }
}
