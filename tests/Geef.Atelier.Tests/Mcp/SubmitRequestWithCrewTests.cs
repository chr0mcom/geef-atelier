using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Mcp.Tools;

namespace Geef.Atelier.Tests.Mcp;

public sealed class SubmitRequestWithCrewTests
{
    [Fact]
    public async Task SubmitRequest_WithCrewTemplate_PassesTemplateNameToService()
    {
        var svc = new CapturingRunService();
        await SubmitRequestTool.SubmitRequest(svc, "briefing", crewTemplate: "klassik");

        Assert.Equal("klassik", svc.LastCrewTemplateName);
        Assert.Null(svc.LastCustomCrew);
    }

    [Fact]
    public async Task SubmitRequest_WithValidCustomCrewJson_PassesSpecToService()
    {
        var svc        = new CapturingRunService();
        const string json = """
            {
              "executorProfileName": "default-executor",
              "reviewerProfileNames": ["briefing-fidelity"],
              "evaluationStrategy": 0,
              "convergenceOverride": null
            }
            """;

        await SubmitRequestTool.SubmitRequest(svc, "briefing", customCrew: json);

        Assert.NotNull(svc.LastCustomCrew);
        Assert.Null(svc.LastCrewTemplateName);  // custom crew overrides template
    }

    [Fact]
    public async Task SubmitRequest_WithInvalidCustomCrewJson_FallsBackToTemplate()
    {
        var svc = new CapturingRunService();
        await SubmitRequestTool.SubmitRequest(svc, "briefing", crewTemplate: "klassik", customCrew: "not-json{{");

        Assert.Null(svc.LastCustomCrew);
        Assert.Equal("klassik", svc.LastCrewTemplateName);
    }

    [Fact]
    public async Task SubmitRequest_WithNoCrewArgs_PassesNullsToService()
    {
        var svc = new CapturingRunService();
        await SubmitRequestTool.SubmitRequest(svc, "briefing");

        Assert.Null(svc.LastCrewTemplateName);
        Assert.Null(svc.LastCustomCrew);
    }

    private sealed class CapturingRunService : IRunService
    {
        public string? LastCrewTemplateName { get; private set; }
        public CrewSpec? LastCustomCrew { get; private set; }

        public Task<Guid> SubmitRunAsync(
            string briefingText, string configJson, string? createdByUser = null,
            string? crewTemplateName = null, CrewSpec? customCrew = null,
            CancellationToken cancellationToken = default)
        {
            LastCrewTemplateName = crewTemplateName;
            LastCustomCrew       = customCrew;
            return Task.FromResult(Guid.NewGuid());
        }

        public Task<RunEntity?> GetRunAsync(Guid runId, CancellationToken ct = default) => Task.FromResult<RunEntity?>(null);
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<RunEntity>>([]);
        public Task<bool> CancelRunAsync(Guid runId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, CancellationToken ct = default) => Task.FromResult<RunDetails?>(null);
        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, CancellationToken ct = default) => Task.FromResult<RunWithGroundingViewModel?>(null);
    }
}
