using System.Collections.Concurrent;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Llm;
using Geef.Atelier.Tests.Pipeline;
using Geef.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// Runs the direct path as the orchestrator does (<see cref="DirectPipelineAssembler"/> → SDK runner →
/// <c>OnRunFinishedAsync</c>) against the recording fake of the end-to-end scaffold. The writer talks to the fake over
/// the real <see cref="OpenAiCompatibleClient"/>, so the recorded request bodies are the wire format the cli-proxy gets.
/// Orders, sources, usages and reports live in memory until the persistence of stage 1 is merged.
/// </summary>
internal sealed class DirectPipelineHarness
{
    public const string Endpoint = "http://cli-proxy.e2e.invalid:8090/v1/claude";

    public DirectPipelineHarness(SourceOptions? sourceOptions = null)
    {
        Llm = new LlmTrafficRecorder();
        Script = new ScriptedLlmEndpoint(Llm);
        SourceOptions = sourceOptions ?? new SourceOptions();

        var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            FailoverHeaders.Enqueue(request.Headers.TryGetValues(OpenAiCompatibleClient.FailoverHeader, out var values)
                ? string.Join(",", values)
                : null);
            return await Script.HandleAsync(request, ct);
        });
        var client = new OpenAiCompatibleClient(new HttpClient(handler), Endpoint, "e2e-key");
        Resolver = new TestLlmClientResolver(client);

        var services = new ServiceCollection();
        services.AddSingleton<IOrderRepository>(Orders);
        services.AddSingleton<ISourceRepository>(Sources);
        services.AddSingleton<IRunReportWriter>(Reports);
        services.AddSingleton<IGroundingConsultationRepository>(Consultations);
        Services = services.BuildServiceProvider();
    }

    public LlmTrafficRecorder Llm { get; }
    public ScriptedLlmEndpoint Script { get; }
    public SourceOptions SourceOptions { get; }
    public ILlmClientResolver Resolver { get; }
    public ServiceProvider Services { get; }
    public InMemoryOrderRepository Orders { get; } = new();
    public InMemorySourceRepository Sources { get; } = new();
    public RecordingRunReportWriter Reports { get; } = new();
    public RecordingConsultationRepository Consultations { get; } = new();
    public InMemoryRunWarnings Warnings { get; } = new();

    /// <summary>Value of <c>x-cli-proxy-failover</c> per LLM request, in order (<c>null</c> = header not sent).</summary>
    public ConcurrentQueue<string?> FailoverHeaders { get; } = new();

    /// <summary>The direct template as stored in a run snapshot.</summary>
    public static CrewSnapshot DirectSnapshot { get; } = new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.DirectTemplateName,
        Executor: SystemCrew.DirectWriterProfile,
        Reviewers: [SystemCrew.DeterministicChecksProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: SystemCrew.DirectTemplate.ConvergenceOverride,
        Advisors: Array.Empty<AdvisorProfile>(),
        CrewDomain: SystemCrew.DirectTemplate.Domain);

    /// <summary>Creates a run with an order for <paramref name="request"/>.</summary>
    public (RunEntity Run, Guid OrderId) CreateRun(string request, string? seedDraft = null)
    {
        var runId = Guid.NewGuid();
        var order = Orders.Add(request, runId);
        var run = new RunEntity
        {
            Id = runId, CreatedAt = DateTimeOffset.UtcNow, Status = RunStatus.Running, BriefingText = request, ConfigJson = "{}",
            CrewTemplateName = SystemCrew.DirectTemplateName, OrderId = order.Id, OrderVersion = 1, SeedDraftText = seedDraft,
        };
        return (run, order.Id);
    }

    public PipelineAssemblyContext Context(RunEntity run) => new(
        run, DirectSnapshot, Services, Services.GetRequiredService<IServiceScopeFactory>(), Resolver,
        Options.Create(new ConvergenceOptions()), NullLoggerFactory.Instance, new CountingEventSink(), null, Warnings, null, null);

    /// <summary>Builds and runs the pipeline, then lets the assembler store the report, like the orchestrator.</summary>
    public async Task<DirectRunOutcome> RunAsync(RunEntity run)
    {
        var assembler = new DirectPipelineAssembler(Options.Create(SourceOptions));
        var context = Context(run);
        var result = await assembler.Assemble(context).RunAsync(run.BriefingText, CancellationToken.None);
        await assembler.OnRunFinishedAsync(context, result, CancellationToken.None);
        Reports.Reports.TryGetValue(run.Id, out var json);
        return new DirectRunOutcome(result, RunReportJson.TryParse(json));
    }

    public IReadOnlyList<RecordedLlmCall> WriterCalls => Llm.CallsFor(LlmRole.Executor);
}

internal sealed record DirectRunOutcome(GeefPipelineResult<FinalizedDocument> Result, RunReport? Report);
