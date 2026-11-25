using System.Runtime.CompilerServices;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>
/// The direct path: <see cref="OrderGroundingStep"/> (the order's files in full text and, for the version of the run, the passages looked
/// up on the web), <see cref="DirectWriterExecutor"/>
/// and <see cref="DeterministicChecksReviewer"/>, at most two passes. After the run it stores the report
/// (<c>Runs.ReportJson</c>). Accepts runs of the code-only template <see cref="SystemCrew.DirectTemplateName"/>, also
/// with a seed draft. A run that changes an earlier version (<see cref="RunEntity.RevisionHint"/>) passes the change to the grounding
/// step, and its report also reads the report of that version (<see cref="RunEntity.ParentRunId"/>, <see cref="EarlierVersionAsync"/>).
/// </summary>
internal sealed class DirectPipelineAssembler(IOptions<SourceOptions> sourceOptions, IOptions<DirectOptions> directOptions) : IPipelineAssembler
{
    private readonly SourceOptions _sourceOptions = sourceOptions.Value;
    private readonly DirectOptions _directOptions = directOptions.Value;

    private static readonly ConditionalWeakTable<PipelineAssemblyContext, FactCheckLedger> Ledgers = new();

    /// <inheritdoc/>
    public int Priority => 90;

    /// <inheritdoc/>
    public bool CanAssemble(RunEntity run, CrewSnapshot snapshot) => snapshot.TemplateName == SystemCrew.DirectTemplateName;

    /// <inheritdoc/>
    public GeefPipelineRunner<FinalizedDocument> Assemble(PipelineAssemblyContext context)
    {
        if (context.Run.OrderId is not { } orderId)
            throw new DirectPathException(DirectPathException.NoOrder);

        var grounding = new OrderGroundingStep(
            context.Run.Id, orderId, context.Run.SeedDraftText, context.ScopeFactory, _sourceOptions, context.WarningSink,
            context.LoggerFactory.CreateLogger<OrderGroundingStep>(), context.Run.OrderVersion, revisionHint: context.Run.RevisionHint);
        var writer = new DirectWriterExecutor(
            context.Snapshot.Executor, context.LlmClientResolver, context.PricingCatalog, context.CostAccumulator,
            context.WarningSink, context.Run.Id, context.LoggerFactory.CreateLogger<DirectWriterExecutor>(),
            WriterNotesSources.For(_directOptions));
        var checks = new DeterministicChecksReviewer();
        var ledger = Ledgers.GetValue(context, _ => new FactCheckLedger());

        return AtelierPipelineFactory.Build(
            context.Snapshot, context.LlmClientResolver, context.ConvergenceOptions,
            runId: context.Run.Id,
            loggerFactory: context.LoggerFactory,
            additionalSinks: [context.EventSink],
            groundingProviderFactory: context.GroundingProviderFactory,
            pricingCatalog: context.PricingCatalog,
            costAccumulator: context.CostAccumulator,
            customExecutionStep: writer,
            specialReviewerResolver: profile => profile.Name switch
            {
                DeterministicChecksReviewer.ReviewerName => checks,
                SourceFactCheckReviewer.ReviewerName => FactReviewer(context, profile, ledger),
                _ => null,
            },
            warningSink: context.WarningSink,
            customGroundingStep: grounding);
    }

    /// <summary>
    /// The fact check of one run: a <see cref="LlmFactChecker"/> with the run's cost accumulator, the run's ledger and, as the one
    /// second attempt, the next model of the slot that is neither the writer's nor from the provider that failed.
    /// </summary>
    private SourceFactCheckReviewer FactReviewer(PipelineAssemblyContext context, ReviewerProfile profile, FactCheckLedger ledger)
    {
        var checker = new LlmFactChecker(context.LlmClientResolver, _directOptions, context.PricingCatalog, context.CostAccumulator,
            context.LoggerFactory.CreateLogger<LlmFactChecker>());
        var writer = context.Snapshot.Executor;
        var domain = context.Snapshot.CrewDomain ?? ModelDomain.General;
        return new SourceFactCheckReviewer(profile, checker, ledger,
            failed => FactCheckModelFallback.For(failed, writer.Provider, writer.Model, domain),
            _directOptions, context.Run.Id, context.LoggerFactory.CreateLogger<SourceFactCheckReviewer>());
    }

    /// <inheritdoc/>
    public async Task OnRunFinishedAsync(PipelineAssemblyContext context, GeefPipelineResult<FinalizedDocument> result, CancellationToken ct)
    {
        var text = result.Output?.Markdown ?? string.Empty;
        var finalContext = result.FinalContext;
        var constraints = finalContext is not null && finalContext.TryGet(AtelierContextKeys.OrderConstraints, out var c) && c is not null
            ? c
            : OrderConstraints.None;
        ReadEvidence? evidence = finalContext is not null && finalContext.TryGet(AtelierContextKeys.ReadEvidence, out var e) ? e : null;

        IReadOnlyList<RunSourceUsageEntity> usages;
        await using (var scope = context.ScopeFactory.CreateAsyncScope())
        {
            usages = await scope.ServiceProvider.GetRequiredService<ISourceRepository>().ListRunUsagesAsync(context.Run.Id, ct);
        }

        var delivered = result.ToDeliveredOutcome();
        var facts = Facts(context, result, delivered.Iteration) with { Revision = await EarlierVersionAsync(context, ct) };
        var report = DirectReportBuilder.Build(constraints, text, usages, delivered.Iteration, result.TotalIterations, evidence, facts);

        await context.RunServices.GetRequiredService<IRunReportWriter>().WriteAsync(context.Run.Id, RunReportJson.Serialize(report), ct);
    }

    /// <summary>
    /// The earlier version a run changes, read from the run of <see cref="RunEntity.ParentRunId"/>; <c>null</c> for a run that is no
    /// change (<see cref="RunEntity.RevisionHint"/> blank). A run that is gone gives no version; a stored report that cannot be read, or
    /// a failed read, is marked <see cref="DirectRevision.ReportUnreadable"/> and never ends the run. <see cref="DirectRevision.NeedsCheck"/>
    /// says whether that run was shown as "Ready – please check" (<see cref="OrderStateResolver"/>).
    /// </summary>
    internal static async Task<DirectRevision?> EarlierVersionAsync(PipelineAssemblyContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.Run.RevisionHint))
            return null;
        if (context.Run.ParentRunId is not { } parentId)
            return new DirectRevision(null, null);

        try
        {
            await using var scope = context.ScopeFactory.CreateAsyncScope();
            if (await scope.ServiceProvider.GetRequiredService<IRunRepository>().GetByIdAsync(parentId, ct) is not { } parent)
                return new DirectRevision(null, null);
            var needsCheck = OrderStateResolver.Resolve(OrderState.Ready, parent.ToRunState(DateTimeOffset.UtcNow)).State == OrderDisplayState.DeliveredWithOpenPoints;
            if (string.IsNullOrWhiteSpace(parent.ReportJson))
                return new DirectRevision(parent.OrderVersion, null, NeedsCheck: needsCheck);
            return RunReportJson.TryParse(parent.ReportJson) is { } report
                ? new DirectRevision(parent.OrderVersion, report, NeedsCheck: needsCheck)
                : new DirectRevision(parent.OrderVersion, null, ReportUnreadable: true, NeedsCheck: needsCheck);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            context.LoggerFactory.CreateLogger<DirectPipelineAssembler>().LogError(
                "Run {RunId}: the report of the earlier version could not be read: {Error}", context.Run.Id, ex.ToRedactedLogText());
            return new DirectRevision(null, null, ReportUnreadable: true);
        }
    }

    /// <summary>
    /// What the report takes from the run: the writer's notes of the delivered round (from its iteration record, else from the
    /// final context), the files, the material, the mode and the totals from the grounding step, the fact-check rounds of the
    /// run's ledger and the codes of the checks without a model that blocked in round 1.
    /// </summary>
    internal static DirectRunFacts Facts(PipelineAssemblyContext context, GeefPipelineResult<FinalizedDocument> result, int? deliveredIteration)
    {
        var finalContext = result.FinalContext;
        var deliveredContext = result.History.Records.FirstOrDefault(r => r.Iteration == deliveredIteration)?.Context ?? finalContext;
        WriterNotesSplit? notes = deliveredContext is not null && deliveredContext.TryGet(AtelierContextKeys.WriterNotes, out var n) ? n : null;
        OrderSourceBundle? bundle = finalContext is not null && finalContext.TryGet(AtelierContextKeys.OrderSources, out var b) ? b : null;
        FactMaterial? material = finalContext is not null && finalContext.TryGet(AtelierContextKeys.FactMaterial, out var m) ? m : null;
        FactCheckMode? mode = finalContext is not null && finalContext.TryGet(AtelierContextKeys.FactCheckMode, out var fm) ? fm : null;
        SourceArithmeticResult? arithmetic = finalContext is not null && finalContext.TryGet(AtelierContextKeys.SourceArithmetic, out var a) ? a : null;

        var firstRound = result.History.Records.FirstOrDefault(r => r.Iteration == 1)?.EvaluationResult.AllFindings
            .Where(f => f.Category == DeterministicChecksReviewer.FindingCategory && f.Severity >= Geef.Sdk.Results.FindingSeverity.Error)
            .Select(f => f.Metadata is not null && f.Metadata.TryGetValue(FactFindingMetadata.Code, out var code) ? code?.ToString() : null)
            .OfType<string>()
            .ToList();

        return new DirectRunFacts(
            notes,
            bundle?.Sources,
            material,
            mode,
            Ledgers.TryGetValue(context, out var ledger) ? ledger.Rounds : [],
            arithmetic,
            firstRound);
    }
}
