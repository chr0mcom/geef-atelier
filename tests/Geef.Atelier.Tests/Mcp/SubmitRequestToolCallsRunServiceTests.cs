using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

public sealed class SubmitRequestToolCallsRunServiceTests
{
    [Fact]
    public async Task SubmitRequest_CallsRunService_WithMcpClientAsCreatedByUser()
    {
        var fakeService = new FakeRunService();
        var result = await SubmitRequestTool.SubmitRequest(
            fakeService,
            briefingText: "test briefing",
            configJson: null,
            cancellationToken: default);

        Assert.Equal("mcp-client", fakeService.LastCreatedByUser);
        Assert.Equal("test briefing", fakeService.LastBriefingText);
        Assert.Equal("Pending", result.Status);
    }

    private sealed class FakeRunService : IRunService
    {
        public string? LastCreatedByUser { get; private set; }
        public string? LastBriefingText { get; private set; }

        public Task<Guid> SubmitRunAsync(
            string briefingText,
            string configJson,
            string? createdByUser = null,
            string? crewTemplateName = null,
            CrewSpec? customCrew = null,
            CancellationToken cancellationToken = default)
        {
            LastCreatedByUser = createdByUser;
            LastBriefingText = briefingText;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<RunEntity?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult<RunEntity?>(null);

        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<RunEntity>>(Array.Empty<RunEntity>());

        public Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, CancellationToken cancellationToken = default)
            => Task.FromResult<RunDetails?>(null);
    }
}
