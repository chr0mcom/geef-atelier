using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk;
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
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> Build(
        CrewSnapshot snapshot,
        ILlmClientResolver resolver,
        IOptions<ConvergenceOptions> convergenceOptions,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null)
    {
        var grounding = new BriefingGroundingStep();
        var execution = new ProfileBasedExecutor(snapshot.Executor, resolver);
        var reviewers = snapshot.Reviewers
            .Select(r => (IReviewer)new ProfileBasedReviewer(r, resolver));
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
