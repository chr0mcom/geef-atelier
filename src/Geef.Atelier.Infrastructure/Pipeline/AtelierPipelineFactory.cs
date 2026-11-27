using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Sdk;
using Geef.Sdk.Events;
using Geef.Sdk.Middleware;
using Geef.Sdk.Policies;
using Geef.Sdk.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SdkGeef = Geef.Sdk.Geef;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class AtelierPipelineFactory
{
    /// <summary>Shown when a stored setup names a check that this version no longer has; the run fails with it as it is.</summary>
    internal const string UnknownCheckMessage =
        "The saved setup of this task names a check that no longer exists, so it cannot run. Please start it again.";

    /// <summary>
    /// Builds the pipeline of one run from its <see cref="CrewSnapshot"/>: the given grounding and execution steps, one reviewer per
    /// reviewer profile of the snapshot (each resolved by <paramref name="reviewerResolver"/>), the convergence policy of the snapshot and
    /// the best-effort text when the checks never pass. A snapshot without reviewers, or with a reviewer the resolver does not know, is
    /// rejected with <see cref="InvalidCrewSnapshotException"/>, whose message is shown to the user as it is.
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> Build(
        CrewSnapshot snapshot,
        IOptions<ConvergenceOptions> convergenceOptions,
        IGroundingStep grounding,
        IExecutionStep execution,
        Func<ReviewerProfile, IReviewer?> reviewerResolver,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null)
    {
        var reviewers = ResolveReviewers(snapshot.Reviewers, reviewerResolver);
        var builder = CreateBuilder(grounding, execution, reviewers, new MarkdownFinalizer(), convergenceOptions, snapshot.ConvergenceOverride,
            snapshot.EvaluationStrategy, loggerFactory, additionalSinks);
        builder.EnableBestEffortOnNonConvergence();
        return builder.Build();
    }

    /// <summary>Builds the pipeline with explicitly supplied steps. Used in tests to inject stubs or fakes.</summary>
    public static GeefPipelineRunner<FinalizedDocument> BuildWithProviders(
        IGroundingStep grounding,
        IExecutionStep execution,
        IEnumerable<IReviewer> reviewers,
        IFinalizer<FinalizedDocument> finalizer,
        IOptions<ConvergenceOptions> convergenceOptions,
        ConvergencePolicyOverride? convergenceOverride = null,
        EvaluationStrategy evaluationStrategy = EvaluationStrategy.Parallel,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null) =>
        CreateBuilder(grounding, execution, reviewers, finalizer, convergenceOptions, convergenceOverride,
            evaluationStrategy, loggerFactory, additionalSinks).Build();

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
        IConvergencePolicy policy = ConvergencePolicyBuilder.Build(convergenceOptions.Value, convergenceOverride);

        var builder = SdkGeef.CreatePipeline<FinalizedDocument>()
            .UseGrounding(grounding)
            .UseExecution(execution);

        foreach (var reviewer in reviewers)
            builder = builder.AddReviewer(reviewer);

        builder = builder
            .UseFinalizer(finalizer)
            .UseConvergencePolicy(policy)
            .UseEvaluationStrategy(EvaluationStrategyMapper.Map(evaluationStrategy))
            .UseMiddleware(new ExceptionHandlingMiddleware())
            .UseMiddleware(new TracingMiddleware());

        if (loggerFactory is not null)
            builder = builder.AddEventSink(new LoggingEventSink(loggerFactory.CreateLogger("Geef.Atelier.Pipeline")));

        if (additionalSinks is not null)
        {
            foreach (var sink in additionalSinks)
                builder = builder.AddEventSink(sink);
        }

        return builder;
    }

    private static List<IReviewer> ResolveReviewers(IReadOnlyList<ReviewerProfile> profiles, Func<ReviewerProfile, IReviewer?> reviewerResolver)
    {
        if (profiles.Count == 0)
            throw new InvalidCrewSnapshotException(
                "The saved setup of this task has no checks, so its text could not be checked. Please start it again.");

        return profiles.Select(p => reviewerResolver(p) ?? throw new InvalidCrewSnapshotException(UnknownCheckMessage)).ToList();
    }
}
