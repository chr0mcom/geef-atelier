using Geef.Atelier.Core.Domain;
using Geef.Sdk;
using Geef.Sdk.Events;
using Geef.Sdk.Middleware;
using Geef.Sdk.Policies;
using Geef.Sdk.Results;
using Microsoft.Extensions.Logging;
using SdkGeef = Geef.Sdk.Geef;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class StubPipelineFactory
{
    public static GeefPipelineRunner<FinalizedDocument> Build(
        ILoggerFactory? loggerFactory = null,
        IEnumerable<IGeefEventSink>? additionalSinks = null)
    {
        var builder = SdkGeef.CreatePipeline<FinalizedDocument>()
            .UseGrounding(new BriefingGroundingStep())
            .UseExecution(new StubExecutionStep())
            .AddReviewer(new StubReviewer(
                "BriefingTreueStub",
                Geef.Sdk.Results.FindingSeverity.Error,
                "Stub finding: simulated briefing-coverage gap (will be cleared on next iteration)."))
            .AddReviewer(new StubReviewer(
                "KlarheitStub",
                Geef.Sdk.Results.FindingSeverity.Warning,
                "Stub finding: simulated clarity nit (will be cleared on next iteration)."))
            .UseFinalizer(new MarkdownFinalizer())
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
