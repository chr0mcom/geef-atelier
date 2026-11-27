using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>Run outcomes over the real wiring: converging, non-converging, and submission over MCP.</summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class RunLifecycleEndToEndTests(PostgresFixture postgres)
{
    private const string Briefing =
        "Write three sentences that explain to new staff why the office closes early on Fridays.";

    [Fact]
    public async Task Simple_request_completes_with_the_executor_text_and_persisted_reviews()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);

        var runId = await host.SubmitAsync(new SubmitRunRequest(
            BriefingText: Briefing,
            ConfigJson: "{}",
            CrewTemplateName: SystemCrew.KlassikTemplateName));
        var run = await host.WaitForTerminalAsync(runId);

        Assert.True(run.Status == RunStatus.Completed, $"Status {run.Status}, error '{run.ErrorMessage}'.\n{host.Llm.Describe()}");
        Assert.Equal(ScriptedLlmEndpoint.DefaultExecutorText, run.FinalText);

        var iterations = await IterationsAsync(host, runId);
        Assert.Single(iterations);
        Assert.Equal(ScriptedLlmEndpoint.DefaultExecutorText, iterations[0].ArtifactText);

        var reviewers = await ReviewerNamesAsync(host, runId);
        Assert.Contains(SystemCrew.BriefingFidelityProfile.Name, reviewers);
        Assert.Contains(SystemCrew.ClarityProfile.Name, reviewers);

        Assert.Single(host.Llm.CallsFor(LlmRole.Executor));
        Assert.Equal(2, host.Llm.CallsFor(LlmRole.Reviewer).Count);
    }

    [Fact]
    public async Task Request_without_convergence_fails_but_delivers_the_text()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        host.Script.ReviewerVerdict = ReviewerVerdict.AlwaysBlock;

        var runId = await host.SubmitAsync(new SubmitRunRequest(
            BriefingText: Briefing,
            ConfigJson: "{}",
            CrewTemplateName: SystemCrew.KlassikTemplateName));
        var run = await host.WaitForTerminalAsync(runId);

        Assert.True(run.Status == RunStatus.Failed, $"Status {run.Status}, error '{run.ErrorMessage}'.\n{host.Llm.Describe()}");
        Assert.Equal(ScriptedLlmEndpoint.DefaultExecutorText, run.FinalText);
        Assert.False(string.IsNullOrWhiteSpace(run.ErrorMessage));

        var iterations = await IterationsAsync(host, runId);
        Assert.True(iterations.Count >= 2, $"Expected several revision rounds, got {iterations.Count}.");
        Assert.Equal(iterations.Count, host.Llm.CallsFor(LlmRole.Executor).Count);
    }

    private static Task<List<IterationEntity>> IterationsAsync(AtelierEndToEndHost host, Guid runId) =>
        host.QueryAsync(db => db.Iterations.AsNoTracking()
            .Where(i => i.RunId == runId)
            .OrderBy(i => i.IterationNumber)
            .ToListAsync());

    private static Task<List<string>> ReviewerNamesAsync(AtelierEndToEndHost host, Guid runId) =>
        host.QueryAsync(db => db.Findings.AsNoTracking()
            .Where(f => db.Iterations.Any(i => i.Id == f.IterationId && i.RunId == runId))
            .Select(f => f.ReviewerName)
            .Distinct()
            .ToListAsync());
}
