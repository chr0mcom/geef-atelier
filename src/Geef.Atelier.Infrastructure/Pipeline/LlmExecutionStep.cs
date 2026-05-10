using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal sealed class LlmExecutionStep(
    ILlmClient client,
    IOptions<LlmOptions> options) : IExecutionStep
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
            var findingList = string.Join("\n", findings.Select(f => $"- [{f.ReviewerName}] {f.Message}"));
            userPrompt = $"""
                Briefing:
                {brief}

                Your previous draft:
                {prevDraft}

                Reviewer findings to address:
                {findingList}

                Rewrite the text, addressing all findings.
                """;
        }

        var actorCfg  = options.Value.Actors.GetValueOrDefault("Executor");
        var model     = actorCfg?.Model is { Length: > 0 } m ? m : options.Value.DefaultModel;
        var maxTokens = actorCfg?.MaxTokens ?? options.Value.DefaultMaxTokens;

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
