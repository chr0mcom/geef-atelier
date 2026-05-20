using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Grounding;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Events;
using Geef.Sdk.Middleware;
using Geef.Sdk.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SdkGeef = Geef.Sdk.Geef;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class AtelierPipelineFactory
{
    /// <summary>
    /// Builds the pipeline dynamically from a fully-dereferenced <see cref="CrewSnapshot"/>.
    /// When the snapshot contains pre-execution advisors (<see cref="AdvisorTrigger.BeforeFirstExecution"/>
    /// or <see cref="AdvisorTrigger.BeforeEveryExecution"/>), the executor is wrapped with an
    /// <see cref="AdvisorAwareExecutor"/> decorator that consults them before each iteration.
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> Build(
        CrewSnapshot snapshot,
        ILlmClientResolver resolver,
        IOptions<ConvergenceOptions> convergenceOptions,
        IAdvisorConsultationRepository? consultationRepository = null,
        Guid runId = default,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null,
        IGroundingProviderFactory? groundingProviderFactory = null,
        IPricingCatalog? pricingCatalog = null,
        ICostAccumulator? costAccumulator = null,
        IGroundingRefiner? groundingRefiner = null,
        IGroundingConsultationRepository? groundingConsultationRepository = null)
    {
        IGroundingStep grounding = new BriefingGroundingStep();
        if (snapshot.GroundingProviders is { Count: > 0 } && groundingProviderFactory is not null)
        {
            grounding = new MultiProviderGroundingStep(
                grounding, snapshot.GroundingProviders, groundingProviderFactory, runId,
                loggerFactory?.CreateLogger<MultiProviderGroundingStep>()
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MultiProviderGroundingStep>.Instance,
                groundingRefiner,
                groundingConsultationRepository);
        }

        IExecutionStep execution = new ProfileBasedExecutor(snapshot.Executor, resolver, pricingCatalog, costAccumulator);

        var preExecutionAdvisors = snapshot.Advisors
            .Where(a => a.Trigger != AdvisorTrigger.OnConvergenceFailure)
            .ToList();

        if (preExecutionAdvisors.Count > 0 && consultationRepository is not null)
        {
            var advisorInstances = preExecutionAdvisors
                .Select(a => new ProfileBasedAdvisor(a, resolver, consultationRepository, pricingCatalog, costAccumulator))
                .ToList();
            execution = new AdvisorAwareExecutor(execution, advisorInstances, runId);
        }

        var reviewers = snapshot.Reviewers
            .Select(r => (IReviewer)new ProfileBasedReviewer(r, resolver, pricingCatalog, costAccumulator));
        var finalizer = new MarkdownFinalizer();

        return BuildWithProviders(grounding, execution, reviewers, finalizer,
            convergenceOptions, snapshot.ConvergenceOverride,
            snapshot.EvaluationStrategy, loggerFactory, additionalSinks);
    }

    /// <summary>
    /// Builds the pipeline like <see cref="Build"/>, but also injects a pre-formed advisor context
    /// block directly into the initial run context. Used by the orchestrator for convergence-failure
    /// recovery passes where <see cref="AdvisorTrigger.OnConvergenceFailure"/> advisors have already
    /// been consulted and their outputs assembled into <paramref name="advisorContextBlock"/>.
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> BuildWithAdvisorContext(
        CrewSnapshot snapshot,
        ILlmClientResolver resolver,
        IOptions<ConvergenceOptions> convergenceOptions,
        string advisorContextBlock,
        IAdvisorConsultationRepository? consultationRepository = null,
        Guid runId = default,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null,
        IGroundingProviderFactory? groundingProviderFactory = null,
        IPricingCatalog? pricingCatalog = null,
        ICostAccumulator? costAccumulator = null,
        IGroundingRefiner? groundingRefiner = null,
        IGroundingConsultationRepository? groundingConsultationRepository = null)
    {
        IGroundingStep grounding = new AdvisorContextGroundingStep(advisorContextBlock);
        if (snapshot.GroundingProviders is { Count: > 0 } && groundingProviderFactory is not null)
        {
            grounding = new MultiProviderGroundingStep(
                grounding, snapshot.GroundingProviders, groundingProviderFactory, runId,
                loggerFactory?.CreateLogger<MultiProviderGroundingStep>()
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MultiProviderGroundingStep>.Instance,
                groundingRefiner,
                groundingConsultationRepository);
        }

        IExecutionStep execution = new ProfileBasedExecutor(snapshot.Executor, resolver, pricingCatalog, costAccumulator);

        var preExecutionAdvisors = snapshot.Advisors
            .Where(a => a.Trigger != AdvisorTrigger.OnConvergenceFailure)
            .ToList();

        if (preExecutionAdvisors.Count > 0 && consultationRepository is not null)
        {
            var advisorInstances = preExecutionAdvisors
                .Select(a => new ProfileBasedAdvisor(a, resolver, consultationRepository, pricingCatalog, costAccumulator))
                .ToList();
            execution = new AdvisorAwareExecutor(execution, advisorInstances, runId);
        }

        var reviewers = snapshot.Reviewers
            .Select(r => (IReviewer)new ProfileBasedReviewer(r, resolver, pricingCatalog, costAccumulator));
        var finalizer = new MarkdownFinalizer();

        return BuildWithProviders(grounding, execution, reviewers, finalizer,
            convergenceOptions, snapshot.ConvergenceOverride,
            snapshot.EvaluationStrategy, loggerFactory, additionalSinks);
    }

    /// <summary>
    /// Builds the pipeline like <see cref="Build"/>, but also injects the last completed iteration's
    /// artifact text as a seed draft into the initial run context. Used by the orchestrator for resume
    /// runs where <paramref name="seedDraftText"/> is non-null.
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> BuildWithSeedDraft(
        CrewSnapshot snapshot,
        ILlmClientResolver resolver,
        IOptions<ConvergenceOptions> convergenceOptions,
        string seedDraftText,
        IAdvisorConsultationRepository? consultationRepository = null,
        Guid runId = default,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null,
        IGroundingProviderFactory? groundingProviderFactory = null,
        IPricingCatalog? pricingCatalog = null,
        ICostAccumulator? costAccumulator = null,
        IGroundingRefiner? groundingRefiner = null,
        IGroundingConsultationRepository? groundingConsultationRepository = null)
    {
        IGroundingStep grounding = new SeedDraftGroundingStep(seedDraftText);
        if (snapshot.GroundingProviders is { Count: > 0 } && groundingProviderFactory is not null)
        {
            grounding = new MultiProviderGroundingStep(
                grounding, snapshot.GroundingProviders, groundingProviderFactory, runId,
                loggerFactory?.CreateLogger<MultiProviderGroundingStep>()
                    ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MultiProviderGroundingStep>.Instance,
                groundingRefiner,
                groundingConsultationRepository);
        }

        IExecutionStep execution = new ProfileBasedExecutor(snapshot.Executor, resolver, pricingCatalog, costAccumulator);

        var preExecutionAdvisors = snapshot.Advisors
            .Where(a => a.Trigger != AdvisorTrigger.OnConvergenceFailure)
            .ToList();

        if (preExecutionAdvisors.Count > 0 && consultationRepository is not null)
        {
            var advisorInstances = preExecutionAdvisors
                .Select(a => new ProfileBasedAdvisor(a, resolver, consultationRepository, pricingCatalog, costAccumulator))
                .ToList();
            execution = new AdvisorAwareExecutor(execution, advisorInstances, runId);
        }

        var reviewers = snapshot.Reviewers
            .Select(r => (IReviewer)new ProfileBasedReviewer(r, resolver, pricingCatalog, costAccumulator));
        var finalizer = new MarkdownFinalizer();

        return BuildWithProviders(grounding, execution, reviewers, finalizer,
            convergenceOptions, snapshot.ConvergenceOverride,
            snapshot.EvaluationStrategy, loggerFactory, additionalSinks);
    }

    /// <summary>
    /// Builds the pipeline with explicitly supplied providers. Used in tests to inject stubs or fakes.
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> BuildWithProviders(
        IGroundingStep grounding,
        IExecutionStep execution,
        IEnumerable<IReviewer> reviewers,
        IFinalizer<FinalizedDocument> finalizer,
        IOptions<ConvergenceOptions> convergenceOptions,
        ConvergencePolicyOverride? convergenceOverride = null,
        EvaluationStrategy evaluationStrategy = EvaluationStrategy.Parallel,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null)
    {
        var builder = SdkGeef.CreatePipeline<FinalizedDocument>()
            .UseGrounding(grounding)
            .UseExecution(execution);

        foreach (var reviewer in reviewers)
            builder = builder.AddReviewer(reviewer);

        builder = builder
            .UseFinalizer(finalizer)
            .UseConvergencePolicy(ConvergencePolicyBuilder.Build(convergenceOptions.Value, convergenceOverride))
            .UseEvaluationStrategy(EvaluationStrategyMapper.Map(evaluationStrategy))
            .UseMiddleware(new ExceptionHandlingMiddleware())
            .UseMiddleware(new TracingMiddleware());

        if (loggerFactory is not null)
        {
            builder = builder.AddEventSink(
                new LoggingEventSink(loggerFactory.CreateLogger("Geef.Atelier.Pipeline")));
        }

        if (additionalSinks is not null)
        {
            foreach (var sink in additionalSinks)
                builder = builder.AddEventSink(sink);
        }

        return builder.Build();
    }
}
