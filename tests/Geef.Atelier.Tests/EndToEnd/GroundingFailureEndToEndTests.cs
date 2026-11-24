using System.Text.Json;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The core promise of Etappe 0 as one chain over the real wiring (review finding T1): a grounding provider fails,
/// the run still ends normally, the failure is stored as a run warning without technical details, and the real MCP
/// endpoint returns that warning from <c>get_run_status</c> and <c>get_run_result</c>. The run is submitted through the
/// container's <see cref="IRunService"/> and executed by the container's orchestrator, so the test also proves that
/// both receive a working warning path from the production wiring.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class GroundingFailureEndToEndTests(PostgresFixture postgres)
{
    private const string Briefing =
        "Write three sentences that explain to new staff why the office closes early on Fridays.";

    private const string SecretInUpstreamError = "sk-or-v1-e2e0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Failing_web_search_ends_the_run_normally_and_reports_a_warning_in_the_database_and_over_mcp()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Tavily.Failure = new HttpRequestException(
            $"Tavily answered 401: invalid key {SecretInUpstreamError} (tavily.e2e.invalid)");

        var runId = await host.SubmitAsync(new SubmitRunRequest(
            BriefingText: Briefing,
            ConfigJson: "{}",
            CrewTemplateName: SystemCrew.KlassikTemplateName));
        var run = await host.WaitForTerminalAsync(runId);

        Assert.True(run.Status == RunStatus.Completed, $"Status {run.Status}, error '{run.ErrorMessage}'.\n{host.Llm.Describe()}");
        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
        Assert.NotEmpty(host.External.For(FakeTavilyEndpoint.ClientName));

        var stored = await host.QueryAsync(db => db.RunWarnings.AsNoTracking().Where(w => w.RunId == runId).ToListAsync());
        var warning = Assert.Single(stored, w => w.Code == RunWarningCodes.GroundingProviderSkipped);
        Assert.Equal(RunWarningSources.Grounding, warning.Source);
        Assert.Contains($"\"{SystemCrew.TavilyRefinedProfile.DisplayName}\"", warning.Message);
        Assert.All(stored, w => AssertNoTechnicalDetail(w.Message));

        var status = await host.CallMcpToolAsync("get_run_status", new { runId = runId.ToString() });
        AssertWarningOverMcp(status, warning.Message);
        Assert.Equal(nameof(RunOutcome.Converged), status.GetProperty("outcome").GetString());

        var result = await host.CallMcpToolAsync("get_run_result", new { runId = runId.ToString() });
        AssertWarningOverMcp(result, warning.Message);
        Assert.Equal(ScriptedLlmEndpoint.DefaultExecutorText, result.GetProperty("finalText").GetString());
    }

    private static void AssertWarningOverMcp(JsonElement payload, string expectedMessage)
    {
        var warnings = payload.GetProperty("warnings").EnumerateArray().ToList();
        Assert.Contains(warnings, w =>
            w.GetProperty("code").GetString() == RunWarningCodes.GroundingProviderSkipped
            && w.GetProperty("message").GetString() == expectedMessage);
        Assert.DoesNotContain(SecretInUpstreamError, payload.GetRawText());
    }

    private static void AssertNoTechnicalDetail(string message)
    {
        Assert.DoesNotContain(SecretInUpstreamError, message);
        Assert.DoesNotContain("sk-or-v1", message);
        Assert.DoesNotContain("tavily.e2e.invalid", message);
        Assert.DoesNotContain("401", message);
    }
}
