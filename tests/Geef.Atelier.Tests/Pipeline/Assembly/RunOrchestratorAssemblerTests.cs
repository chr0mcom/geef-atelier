using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Orchestrator;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Services;
using Geef.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Pipeline.Assembly;

/// <summary>The orchestrator builds every pipeline through the selected assembler and lets it finish after the outcome is stored.</summary>
[Collection("Postgres")]
public sealed class RunOrchestratorAssemblerTests(PostgresFixture fixture)
{
    private sealed class RecordingAssembler(IPipelineAssembler inner, PostgresFixture fixture) : IPipelineAssembler
    {
        public int Priority => 1000;
        public bool CanAssemble(RunEntity run, global::Geef.Atelier.Core.Domain.Crew.CrewSnapshot snapshot) => true;
        public GeefPipelineRunner<FinalizedDocument> Assemble(PipelineAssemblyContext context) => inner.Assemble(context);
        public RunOutcome? OutcomeSeenWhenFinished { get; private set; }
        public int FinishedCalls { get; private set; }

        public async Task OnRunFinishedAsync(PipelineAssemblyContext context, GeefPipelineResult<FinalizedDocument> result, CancellationToken ct)
        {
            FinishedCalls++;
            await using var db = fixture.NewContext();
            OutcomeSeenWhenFinished = await db.Runs.Where(r => r.Id == context.Run.Id).Select(r => r.Outcome).SingleAsync(ct);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task The_selected_assembler_builds_the_pipeline_and_stores_its_results_before_the_outcome()
    {
        await using var ctx = fixture.NewContext();
        var runId = await new RunPersistenceService(ctx).CreateRunAsync("Assembler test", "{}", crewSnapshotJson: TestCrewSnapshots.RunnableJson, cancellationToken: CancellationToken.None);
        var recording = new RecordingAssembler(new TestLoopAssembler(), fixture);

        await using var host = new OrchestratorTestHost(fixture, new FakeLlmClient(), configureServices: services =>
            services.AddSingleton<IPipelineAssemblerSelector>(new PipelineAssemblerSelector([recording])));
        await host.StartAsync();

        var deadline = DateTime.UtcNow.AddSeconds(20);
        RunEntity? run = null;
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            await using var read = fixture.NewContext();
            run = await read.Runs.AsNoTracking().SingleAsync(r => r.Id == runId);
            if (run.Outcome is not null && recording.FinishedCalls > 0)
                break;
        }
        await host.StopAsync();

        Assert.Equal(RunStatus.Completed, run!.Status);
        Assert.Equal(1, recording.FinishedCalls);
        Assert.Null(recording.OutcomeSeenWhenFinished);
        Assert.Equal((RunOutcome?)RunOutcome.Converged, run.Outcome);
    }

    [Theory]
    [InlineData(DirectPathException.WriterReturnedNoText)]
    [InlineData(DirectPathException.WritingServiceBusy)]
    [InlineData(DirectPathException.NoOrder)]
    public void Direct_path_messages_reach_the_person_unchanged_also_when_wrapped(string message)
    {
        Assert.Equal(message, RunOrchestratorService.SanitizeErrorMessage(new DirectPathException(message)));
        Assert.Equal(message, RunOrchestratorService.SanitizeErrorMessage(new InvalidOperationException("sdk", new DirectPathException(message))));
    }

    [Fact]
    public void Other_exceptions_keep_their_fixed_sentences()
    {
        Assert.Equal(RunOrchestratorService.UnexpectedErrorMessage, RunOrchestratorService.SanitizeErrorMessage(new InvalidOperationException("secret detail")));
    }
}
