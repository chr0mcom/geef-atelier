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
using Geef.Sdk.Runtime;
using Geef.Sdk.Context;
using Geef.Sdk.Events;
using Geef.Sdk.Middleware;
using Geef.Sdk.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SdkGeef = Geef.Sdk.Geef;
using SdkAdvisorTrigger = Geef.Sdk.Advisors.AdvisorTrigger;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class AtelierPipelineFactory
{
    /// <summary>
    /// Builds the pipeline dynamically from a fully-dereferenced <see cref="CrewSnapshot"/>.
    /// Pre-execution advisors (<see cref="AdvisorTrigger.BeforeFirstExecution"/> /
    /// <see cref="AdvisorTrigger.BeforeEveryExecution"/>) and convergence-failure recovery
    /// advisors (<see cref="AdvisorTrigger.OnConvergenceFailure"/>) are registered directly
    /// with the SDK builder and consulted by the runner at the appropriate lifecycle points.
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
        IGroundingConsultationRepository? groundingConsultationRepository = null,
        IExecutionStep? customExecutionStep = null,
        Func<Geef.Atelier.Core.Domain.Crew.Profiles.ReviewerProfile, IReviewer?>? specialReviewerResolver = null)
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

        IExecutionStep execution = customExecutionStep
            ?? new ProfileBasedExecutor(snapshot.Executor, resolver, pricingCatalog, costAccumulator);

        var reviewers = ResolveReviewers(snapshot.Reviewers, resolver, pricingCatalog, costAccumulator, specialReviewerResolver);
        var finalizer = new MarkdownFinalizer();

        var builder = CreateBuilder(grounding, execution, reviewers, finalizer,
            convergenceOptions, snapshot.ConvergenceOverride,
            snapshot.EvaluationStrategy, loggerFactory, additionalSinks);

        if (consultationRepository is not null && customExecutionStep is null)
            RegisterAdvisors(builder, snapshot.Advisors, resolver, consultationRepository, runId, pricingCatalog, costAccumulator);

        builder.EnableBestEffortOnNonConvergence();

        return builder.Build();
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
        var reviewers = ResolveReviewers(snapshot.Reviewers, resolver, pricingCatalog, costAccumulator);
        var finalizer = new MarkdownFinalizer();

        var builder = CreateBuilder(grounding, execution, reviewers, finalizer,
            convergenceOptions, snapshot.ConvergenceOverride,
            snapshot.EvaluationStrategy, loggerFactory, additionalSinks);

        if (consultationRepository is not null)
            RegisterAdvisors(builder, snapshot.Advisors, resolver, consultationRepository, runId, pricingCatalog, costAccumulator);

        builder.EnableBestEffortOnNonConvergence();

        return builder.Build();
    }

    /// <summary>
    /// Builds the pipeline with explicitly supplied providers. Used in tests to inject stubs or fakes.
    /// Advisor registration is skipped (no advisor profiles available in this code path).
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
        return CreateBuilder(grounding, execution, reviewers, finalizer,
            convergenceOptions, convergenceOverride,
            evaluationStrategy, loggerFactory, additionalSinks).Build();
    }

    private static GeefPipelineBuilder<FinalizedDocument> CreateBuilder(
        IGroundingStep grounding,
        IExecutionStep execution,
        IEnumerable<IReviewer> reviewers,
        IFinalizer<FinalizedDocument> finalizer,
        IOptions<ConvergenceOptions> convergenceOptions,
        ConvergencePolicyOverride? convergenceOverride,
        EvaluationStrategy evaluationStrategy,
        ILoggerFactory? loggerFactory,
        IEnumerable<IGeefEventSink>? additionalSinks)
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

        return builder;
    }

    private static void RegisterAdvisors(
        GeefPipelineBuilder<FinalizedDocument> builder,
        IReadOnlyList<AdvisorProfile> profiles,
        ILlmClientResolver resolver,
        IAdvisorConsultationRepository consultationRepository,
        Guid runId,
        IPricingCatalog? pricingCatalog,
        ICostAccumulator? costAccumulator)
    {
        foreach (var profile in profiles)
        {
            var advisor = new ProfileBasedAdvisor(
                profile, resolver, consultationRepository, runId, pricingCatalog, costAccumulator);
            builder.AddAdvisor(advisor, MapTrigger(profile.Trigger));
        }
    }

    private static SdkAdvisorTrigger MapTrigger(AdvisorTrigger atelierTrigger) => atelierTrigger switch
    {
        AdvisorTrigger.BeforeFirstExecution => SdkAdvisorTrigger.BeforeFirstExecution,
        AdvisorTrigger.BeforeEveryExecution => SdkAdvisorTrigger.BeforeEveryExecution,
        AdvisorTrigger.OnConvergenceFailure => SdkAdvisorTrigger.OnConvergenceFailure,
        _ => SdkAdvisorTrigger.BeforeFirstExecution
    };

    /// <summary>
    /// Returns reviewers for the given profiles, falling back to <see cref="AutoApproveReviewer"/> when
    /// the profile list is empty (single-pass / reviewer-free template).
    /// When <paramref name="specialReviewerResolver"/> is supplied, each profile is offered to it first;
    /// a non-null return value replaces the default <see cref="ProfileBasedReviewer"/> for that profile.
    /// </summary>
    private static IEnumerable<IReviewer> ResolveReviewers(
        IReadOnlyList<Geef.Atelier.Core.Domain.Crew.Profiles.ReviewerProfile> profiles,
        ILlmClientResolver resolver,
        IPricingCatalog? pricingCatalog,
        ICostAccumulator? costAccumulator,
        Func<Geef.Atelier.Core.Domain.Crew.Profiles.ReviewerProfile, IReviewer?>? specialReviewerResolver = null)
    {
        if (profiles.Count == 0)
            return [new AutoApproveReviewer()];

        var classifier = new HttpTransientFaultClassifier();
        return profiles.Select(r =>
        {
            if (specialReviewerResolver is not null)
            {
                var special = specialReviewerResolver(r);
                if (special is not null)
                    return special;
            }

            return (IReviewer)new ResilientReviewer(
                new ProfileBasedReviewer(r, resolver, pricingCatalog, costAccumulator),
                classifier,
                maxAttempts: LlmResilience.ReviewerMaxAttempts,
                maxDelay: LlmResilience.ReviewerMaxDelay);
        });
    }
}
