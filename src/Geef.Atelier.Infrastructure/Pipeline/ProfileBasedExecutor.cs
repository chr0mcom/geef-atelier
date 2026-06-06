using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal sealed class ProfileBasedExecutor(
    ExecutorProfile profile,
    ILlmClientResolver resolver,
    IPricingCatalog? pricingCatalog = null,
    ICostAccumulator? costAccumulator = null) : IExecutionStep
{
    // Revision iterations must re-emit the whole artifact. If the executor instead returns a
    // change-summary / cover letter (a known LLM failure mode), the resulting text collapses to a
    // fraction of the previous draft. These thresholds detect that collapse so it can be retried.
    private const int    MinComparableDraftLength = 2000;
    private const double RegressionRatio          = 0.5;

    public async Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var brief = context.GetRequired(AtelierContextKeys.GroundedBrief);
        var iter  = context.GetRequired(GeefKeys.CurrentIteration);

        var (client, model, maxTokens) = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);

        string? prevDraft = null;
        string  findingList = "";
        string  userPrompt;
        if (iter == 1)
        {
            if (context.TryGet(AtelierContextKeys.SeedDraft, out var seedDraft) && seedDraft is not null)
            {
                userPrompt = $"""
                    Briefing:
                    {brief}

                    Previous draft (from an interrupted run — revise and improve it):
                    {seedDraft}

                    Revise the draft to better fulfill the briefing. Improve quality, address any
                    weaknesses you can identify, and make the text more polished. Output the COMPLETE
                    revised document in full — only the document itself, no commentary.
                    """;
            }
            else
            {
                userPrompt = $"Briefing:\n{brief}\n\nWrite a text according to the briefing.";
            }
        }
        else
        {
            prevDraft = context.GetRequired(AtelierContextKeys.CurrentDraft);
            var findings = ExtractPreviousFindings(context);
            var findingLines = findings
                .Select((f, i) => $"{i + 1}. [{f.Severity.ToString().ToUpperInvariant()}] [{f.ReviewerName}] {f.Message}")
                .ToList();
            findingList = findingLines.Count > 0
                ? string.Join("\n", findingLines)
                : "(no findings — improve general quality)";
            userPrompt = BuildRevisionPrompt(brief, prevDraft, findingList, forceful: false);
        }

        userPrompt = PrependContextBlocks(context, userPrompt);

        var response = await CompleteAndRecordAsync(client, model, userPrompt, iter, cancellationToken);

        // Safety net: never let a change-summary / cover-letter response (a collapse far below the
        // previous draft) become the new state. Retry once forcefully; if it still collapses, keep
        // whichever text preserves the most content so the run never regresses below its best draft.
        var resultText  = response.Text;
        var tokenUsage  = response.TokenUsage;
        if (prevDraft is not null && IsRegression(resultText, prevDraft))
        {
            var retryPrompt = PrependContextBlocks(
                context, BuildRevisionPrompt(brief, prevDraft, findingList, forceful: true));
            var retry = await CompleteAndRecordAsync(client, model, retryPrompt, iter, cancellationToken);

            if (!IsRegression(retry.Text, prevDraft))
            {
                resultText = retry.Text;
                tokenUsage = retry.TokenUsage;
            }
            else
            {
                // Both attempts collapsed — keep the longest available text (the prior full draft
                // wins over two short changelogs), guaranteeing no regression in document state.
                resultText = new[] { resultText, retry.Text, prevDraft }.MaxBy(t => t.Length)!;
                tokenUsage = retry.TokenUsage;
            }
        }

        var updated = context
            .Set(AtelierContextKeys.CurrentDraft, resultText)
            .Set(AtelierContextKeys.TokenUsage, tokenUsage);
        return new ExecutionResult
        {
            UpdatedContext = updated,
            Notes = [$"tokens_in={tokenUsage.InputTokens} tokens_out={tokenUsage.OutputTokens}"]
        };
    }

    private string PrependContextBlocks(IRunContext context, string userPrompt)
    {
        // Prepend grounding context (web research) before advisor block and user prompt.
        if (context.TryGet(AtelierContextKeys.GroundingContext, out var groundingCtx) && groundingCtx is not null)
            userPrompt = $"{groundingCtx}\n\n{userPrompt}";

        // Prepend advisor consultation outputs to the user prompt when present.
        if (context.TryGet(AtelierContextKeys.AdvisorBlock, out var advisorBlock) && advisorBlock is not null)
            userPrompt = $"{advisorBlock}\n\n{userPrompt}";

        return userPrompt;
    }

    private async Task<LlmResponse> CompleteAndRecordAsync(
        ILlmClient client, string model, string userPrompt, int iter, CancellationToken cancellationToken)
    {
        var response = await client.CompleteAsync(new LlmRequest
        {
            Model        = model,
            SystemPrompt = profile.SystemPrompt,
            UserPrompt   = userPrompt,
            MaxTokens    = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens).MaxTokens
        }, cancellationToken);

        if (costAccumulator is not null)
        {
            var costEur = pricingCatalog?.CalculateCostEur(
                model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, profile.Provider);
            costAccumulator.RecordActorCost(
                iter, ActorType.Executor, profile.Name, model,
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, costEur,
                providerName: profile.Provider);
        }

        return response;
    }

    private static string BuildRevisionPrompt(string brief, string prevDraft, string findingList, bool forceful)
    {
        var emphasis = forceful
            ? "\n\nIMPORTANT: Your previous response was REJECTED because it was a change-summary / "
              + "cover letter / list of edits instead of the document itself. Output the ENTIRE document "
              + "in full this time — every section, in final form — and NOTHING else."
            : "";

        return $$"""
            Briefing:
            {{brief}}

            Your previous draft (this is the current full document):
            {{prevDraft}}

            Reviewer findings — resolve each one with a concrete, visible change:
            {{findingList}}

            Produce the COMPLETE, revised document in full. Reproduce every section and all content
            that should remain, integrating a specific change that resolves each finding above.
            Output ONLY the finished document itself — do NOT output a change-summary, changelog, cover
            letter, response-to-reviewers, or any description of your edits. The result must be a
            standalone document that fully replaces the previous draft, not a description of changes.{{emphasis}}
            """;
    }

    private static bool IsRegression(string newText, string prevDraft)
        => prevDraft.Length >= MinComparableDraftLength
           && newText.Length < prevDraft.Length * RegressionRatio;

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
