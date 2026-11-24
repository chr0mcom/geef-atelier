using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Sdk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>
/// The direct path: <see cref="OrderGroundingStep"/> (the order's files in full text), <see cref="DirectWriterExecutor"/>
/// and <see cref="DeterministicChecksReviewer"/>, at most two passes. After the run it stores the report
/// (<c>Runs.ReportJson</c>). Accepts runs of the code-only template <see cref="SystemCrew.DirectTemplateName"/>, also
/// with a seed draft.
/// </summary>
internal sealed class DirectPipelineAssembler(IOptions<SourceOptions> sourceOptions) : IPipelineAssembler
{
    private readonly SourceOptions _sourceOptions = sourceOptions.Value;

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
            context.LoggerFactory.CreateLogger<OrderGroundingStep>());
        var writer = new DirectWriterExecutor(
            context.Snapshot.Executor, context.LlmClientResolver, context.PricingCatalog, context.CostAccumulator,
            context.WarningSink, context.Run.Id, context.LoggerFactory.CreateLogger<DirectWriterExecutor>(),
            IdentityDraftOutputSplitter.Instance);
        var checks = new DeterministicChecksReviewer();

        return AtelierPipelineFactory.Build(
            context.Snapshot, context.LlmClientResolver, context.ConvergenceOptions,
            runId: context.Run.Id,
            loggerFactory: context.LoggerFactory,
            additionalSinks: [context.EventSink],
            groundingProviderFactory: context.GroundingProviderFactory,
            pricingCatalog: context.PricingCatalog,
            costAccumulator: context.CostAccumulator,
            customExecutionStep: writer,
            specialReviewerResolver: profile => profile.Name == DeterministicChecksReviewer.ReviewerName ? checks : null,
            warningSink: context.WarningSink,
            customGroundingStep: grounding);
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
        var report = DirectReportBuilder.Build(constraints, text, usages, delivered.Iteration, result.TotalIterations, evidence);

        await context.RunServices.GetRequiredService<IRunReportWriter>().WriteAsync(context.Run.Id, RunReportJson.Serialize(report), ct);
    }
}
