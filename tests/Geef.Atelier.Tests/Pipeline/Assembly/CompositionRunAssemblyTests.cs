using System.Collections.Concurrent;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Orchestrator;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Tests.Pipeline.Assembly;

/// <summary>
/// A crew-composition run goes through the orchestrator and the composition assembler like before the assembler split:
/// the composer executor is the execution step (structured <c>submit_crew_spec</c> call with the composer profile's
/// prompt, model and token limit) and <c>crew-spec-validator</c> is the deterministic validator, not an LLM reviewer.
/// </summary>
[Collection("Postgres")]
public sealed class CompositionRunAssemblyTests(PostgresFixture fixture)
{
    private const string Brief = "Compose a crew that writes short notices for tenants about building repairs.";

    private const string NotASpec = "This is prose, not a crew specification.";

    private static readonly CrewSnapshot ComposerSnapshot = new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.CrewComposerTemplateName,
        Executor: SystemCrew.CrewComposerExecutorProfile,
        Reviewers: [SystemCrew.CrewSpecValidatorProfile, SystemCrew.CrewComposerCompletenessProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: SystemCrew.CrewComposerTemplate.ConvergenceOverride,
        Advisors: Array.Empty<AdvisorProfile>());

    [Fact(Timeout = 60_000)]
    public async Task A_composition_run_uses_the_composer_executor_and_the_deterministic_spec_validator()
    {
        Guid runId;
        await using (var ctx = fixture.NewContext())
        {
            runId = await new RunPersistenceService(ctx).CreateRunAsync(
                Brief, "{}", createdByUser: "composer-" + Guid.NewGuid().ToString("N")[..6],
                crewTemplateName: SystemCrew.CrewComposerTemplateName,
                crewSnapshotJson: Fakes.TestCrewSnapshots.Serialize(ComposerSnapshot),
                kind: RunKind.CrewComposition,
                cancellationToken: CancellationToken.None);
        }

        var llm = new CompositionLlm();
        await using var host = new OrchestratorTestHost(fixture, llm, configureServices: services => services.AddCrewComposition());
        await host.StartAsync();
        var run = await WaitForTerminalAsync(runId);
        await host.StopAsync();

        var composerCalls = llm.Requests.Where(r => r.SystemPrompt.StartsWith(SystemCrew.CrewComposerExecutorProfile.SystemPrompt, StringComparison.Ordinal)).ToList();
        Assert.True(composerCalls.Count > 0, Describe(llm, run));
        Assert.All(composerCalls, call =>
        {
            Assert.Equal(CrewSpecTool.ToolName, call.ResponseFormat?.SchemaName);
            Assert.Equal("json_schema", call.ResponseFormat?.Type);
            Assert.Equal(ComposerSnapshot.Executor.Model, call.Model);
            Assert.Equal(SystemCrew.CrewComposerExecutorProfile.MaxTokens, call.MaxTokens);
            Assert.Contains(Brief, call.UserPrompt, StringComparison.Ordinal);
            Assert.Contains("submit_crew_spec", call.UserPrompt, StringComparison.Ordinal);
        });

        Assert.DoesNotContain(llm.Requests, r => r.SystemPrompt.Contains(SystemPrompts.CrewSpecValidator, StringComparison.Ordinal));
        Assert.Contains(llm.Requests, r => r.SystemPrompt.Contains(SystemPrompts.CrewComposerCompleteness, StringComparison.Ordinal));

        await using var read = fixture.NewContext();
        var validatorFindings = await read.Findings.AsNoTracking()
            .Where(f => f.ReviewerName == "crew-spec-validator" && read.Iterations.Any(i => i.Id == f.IterationId && i.RunId == runId))
            .Select(f => f.Message)
            .ToListAsync();
        Assert.Contains("[spec_json] Invalid JSON or missing required fields (mode not recognised).", validatorFindings);
    }

    private async Task<RunEntity> WaitForTerminalAsync(Guid runId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        RunEntity? run = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            await using var read = fixture.NewContext();
            run = await read.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
            if (run.Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Aborted)
                return run;
        }
        throw new TimeoutException($"Composition run did not finish (status {run?.Status}).");
    }

    private static string Describe(CompositionLlm llm, RunEntity run) =>
        $"Run {run.Status} '{run.ErrorMessage}'. Requests:\n" + string.Join("\n", llm.Requests.Select(r =>
            $"- model={r.Model} format={r.ResponseFormat?.SchemaName ?? "-"} tools={string.Join(',', r.Tools?.Select(t => t.Name) ?? [])} system={r.SystemPrompt[..Math.Min(60, r.SystemPrompt.Length)]}"));

    private sealed class CompositionLlm : ILlmClient
    {
        public ConcurrentQueue<LlmRequest> Requests { get; } = new();

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            if (request.ResponseFormat?.SchemaName == CrewSpecTool.ToolName || request.Tools?.Any(t => t.Name == CrewSpecTool.ToolName) == true)
                return Task.FromResult(FakeLlmClient.MakeTextResponse(NotASpec));
            if (request.Tools is { Count: > 0 })
                return Task.FromResult(FakeLlmClient.MakeApproveResponse());
            return Task.FromResult(FakeLlmClient.MakeTextResponse("Free text instead of a crew specification."));
        }
    }
}
