using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Configuration;
using Geef.Atelier.Infrastructure.Llm;
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
    /// <summary>
    /// Builds the pipeline with real LLM providers via the resolver.
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> Build(
        ILlmClientResolver resolver,
        IOptions<ConvergenceOptions> convergenceOptions,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null)
    {
        var grounding  = new BriefingGroundingStep();
        var execution  = new LlmExecutionStep(resolver);
        var reviewers  = new IReviewer[]
        {
            new LlmReviewer("BriefingTreueReviewer", AtelierSystemPrompts.BriefingTreue, resolver),
            new LlmReviewer("KlarheitReviewer",       AtelierSystemPrompts.Klarheit,      resolver)
        };
        var finalizer = new MarkdownFinalizer();
        return BuildWithProviders(grounding, execution, reviewers, finalizer, convergenceOptions, loggerFactory, additionalSinks);
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
            .UseConvergencePolicy(new DefaultConvergencePolicy
            {
                MaxIterations       = convergenceOptions.Value.MaxIterations,
                AbortOnCritical     = convergenceOptions.Value.AbortOnCritical,
                DetectRegression    = convergenceOptions.Value.DetectRegression,
                StagnationThreshold = convergenceOptions.Value.StagnationThreshold
            })
            .UseEvaluationStrategy(new ParallelEvaluationStrategy())
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
