using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal sealed class LlmExecutionStep(ILlmClientResolver resolver) : IExecutionStep
{
    public async Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var brief = context.GetRequired(AtelierContextKeys.GroundedBrief);
        var iter  = context.GetRequired(GeefKeys.CurrentIteration);

        string userPrompt;
        if (iter == 1)
        {
            userPrompt = $"Briefing:\n{brief}\n\nWrite a text according to the briefing.";
        }
        else
        {
            var prevDraft = context.GetRequired(AtelierContextKeys.CurrentDraft);
            var findings  = ExtractPreviousFindings(context);
            var findingLines = findings
                .Select((f, i) => $"{i + 1}. [{f.Severity.ToString().ToUpperInvariant()}] [{f.ReviewerName}] {f.Message}")
                .ToList();
            var findingList = findingLines.Count > 0
                ? string.Join("\n", findingLines)
                : "(no findings — improve general quality)";
            userPrompt = $"""
                Briefing:
                {brief}

                Your previous draft:
                {prevDraft}

                Reviewer findings — address each one with a concrete, visible change:
                {findingList}

                Rewrite the text. For every finding above, make a specific change that resolves it.
                """;
        }

        var (client, model, maxTokens) = resolver.ForActor("Executor");

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = model,
            SystemPrompt = AtelierSystemPrompts.Executor,
            UserPrompt   = userPrompt,
            MaxTokens    = maxTokens
        }, cancellationToken);

        var updated = context
            .Set(AtelierContextKeys.CurrentDraft, response.Text)
            .Set(AtelierContextKeys.TokenUsage, response.TokenUsage);
        return new ExecutionResult
        {
            UpdatedContext = updated,
            Notes = [$"tokens_in={response.TokenUsage.InputTokens} tokens_out={response.TokenUsage.OutputTokens}"]
        };
    }

    private static IReadOnlyList<Finding> ExtractPreviousFindings(IRunContext ctx)
    {
        if (ctx.TryGet(GeefKeys.IterationHistory, out var history)
            && history is not null
            && history.Records.Count > 0)
        {
            return history.Records[^1].EvaluationResult.AllFindings;
        }
        return [];
    }
}
