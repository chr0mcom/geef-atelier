using Geef.Atelier.Core.Domain;
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
    /// Builds the pipeline with real LLM providers. Requires a configured <see cref="IAnthropicClient"/>.
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> Build(
        IAnthropicClient client,
        IOptions<AnthropicOptions> options,
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null)
    {
        var grounding  = new BriefingGroundingStep();
        var execution  = new LlmExecutionStep(client, options);
        var reviewers  = new IReviewer[]
        {
            new LlmReviewer("BriefingTreueReviewer", AtelierSystemPrompts.BriefingTreue, client, options),
            new LlmReviewer("KlarheitReviewer",       AtelierSystemPrompts.Klarheit,      client, options)
        };
        var finalizer = new MarkdownFinalizer();
        return BuildWithProviders(grounding, execution, reviewers, finalizer, loggerFactory, additionalSinks);
    }

    /// <summary>
    /// Builds the pipeline with explicitly supplied providers. Used in tests to inject stubs or fakes.
    /// </summary>
    public static GeefPipelineRunner<FinalizedDocument> BuildWithProviders(
        IGroundingStep grounding,
        IExecutionStep execution,
        IEnumerable<IReviewer> reviewers,
        IFinalizer<FinalizedDocument> finalizer,
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
                MaxIterations       = 3,
                AbortOnCritical     = true,
                DetectRegression    = true,
                StagnationThreshold = 3
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
