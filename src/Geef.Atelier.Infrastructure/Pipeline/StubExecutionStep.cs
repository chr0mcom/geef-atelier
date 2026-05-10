using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal sealed class StubExecutionStep : IExecutionStep
{
    public Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var brief = context.GetRequired(AtelierContextKeys.GroundedBrief);
        var iter = context.GetRequired(GeefKeys.CurrentIteration);

        string output;
        if (iter == 1)
        {
            output = $"DRAFT v1 — Briefing: {brief}\n\n[Stub-Output]";
        }
        else
        {
            // Derive previous findings from IterationHistory (PreviousFindings key type unknown from reflection).
            var findingCount = 0;
            var findingMessages = string.Empty;
            if (context.TryGet(GeefKeys.IterationHistory, out var history)
                && history is not null
                && history.Records.Count > 0)
            {
                var lastRecord = history.Records[^1];
                var findings   = lastRecord.EvaluationResult.AllFindings;
                findingCount   = findings.Count;
                findingMessages = string.Join(", ", findings.Select(f => f.Message));
            }

            output = $"DRAFT v{iter} — addressed {findingCount} findings: {findingMessages}\n\n[Stub-Output]";
        }

        var updated = context.Set(AtelierContextKeys.CurrentDraft, output);
        return Task.FromResult(new ExecutionResult { UpdatedContext = updated, Notes = [] });
    }
}
