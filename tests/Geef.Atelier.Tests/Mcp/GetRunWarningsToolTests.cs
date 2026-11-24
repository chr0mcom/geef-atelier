using System.Text.Json;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp;

/// <summary><c>get_run_status</c> and <c>get_run_result</c> expose warnings and the outcome as additive fields.</summary>
public sealed class GetRunWarningsToolTests
{
    private static readonly Guid RunId = Guid.NewGuid();

    [Fact]
    public async Task StatusCarriesWarningsAndOutcome()
    {
        var result = await GetRunStatusTool.GetRunStatus(new WarningRunService(), new User("owner"), RunId.ToString());

        Assert.NotNull(result);
        var warning = Assert.Single(result!.Warnings!);
        Assert.Equal((RunWarningCodes.GroundingProviderSkipped, "A source could not be read and was skipped."), (warning.Code, warning.Message));
        Assert.Equal("DeliveredWithOpenPoints", result.Outcome);
    }

    [Fact]
    public async Task ResultCarriesWarningsAndOutcome()
    {
        var result = await GetRunResultTool.GetRunResult(new WarningRunService(), new User("owner"), RunId.ToString());

        Assert.NotNull(result);
        Assert.Equal("text", result!.FinalText);
        Assert.Single(result.Warnings!);
        Assert.Equal("DeliveredWithOpenPoints", result.Outcome);
    }

    [Fact]
    public async Task OtherUsersSeeNeitherTheRunNorItsWarnings()
    {
        Assert.Null(await GetRunStatusTool.GetRunStatus(new WarningRunService(), new User("intruder"), RunId.ToString()));
        Assert.Null(await GetRunResultTool.GetRunResult(new WarningRunService(), new User("intruder"), RunId.ToString()));
    }

    [Fact]
    public void SerializesAsAdditiveCamelCaseFields()
    {
        var dto = new RunStatusDto("r", "Failed", "e", [new RunWarningDto("llm.failover", "m")], "DeliveredWithOpenPoints");

        var json = JsonSerializer.Serialize(dto, McpJsonUtilities.DefaultOptions);

        Assert.Contains("\"warnings\":[{\"code\":\"llm.failover\",\"message\":\"m\"}]", json);
        Assert.Contains("\"outcome\":\"DeliveredWithOpenPoints\"", json);
        Assert.Contains("\"errorMessage\":\"e\"", json);
    }

    [Fact]
    public void OldPositionalConstructionStillCompilesAndDefaultsToNoWarnings()
    {
        var dto = new RunResultDto("r", "Completed", "t", null);

        Assert.Null(dto.Warnings);
        Assert.Null(dto.Outcome);
    }

    private sealed class User(string name) : ICurrentUserService
    {
        public string? Username => name;
        public bool IsAuthenticated => true;
        public bool IsAdmin => false;
    }

    private sealed class WarningRunService : IRunService
    {
        private static readonly RunEntity Run = new()
        {
            Id = RunId,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = RunStatus.Failed,
            BriefingText = "b",
            ConfigJson = "{}",
            CreatedByUser = "owner",
            FinalText = "text",
            Outcome = RunOutcome.DeliveredWithOpenPoints,
        };

        public Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) =>
            Task.FromResult(runId == RunId && (requestingUsername is null || requestingUsername == Run.CreatedByUser) ? Run : null);

        public Task<IReadOnlyList<RunWarningEntity>> ListRunWarningsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunWarningEntity>>(requestingUsername is not null && requestingUsername != Run.CreatedByUser
                ? []
                : [new RunWarningEntity
                {
                    Id = Guid.NewGuid(),
                    RunId = runId,
                    Source = RunWarningSources.Grounding,
                    Code = RunWarningCodes.GroundingProviderSkipped,
                    Message = "A source could not be read and was skipped.",
                    CreatedAt = DateTimeOffset.UtcNow,
                }]);

        public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
