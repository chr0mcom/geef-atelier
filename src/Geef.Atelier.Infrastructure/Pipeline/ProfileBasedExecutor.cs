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
    // change-summary / cover letter or an abbreviated partial (known LLM failure modes on long
    // documents), the result collapses well below the most complete draft seen so far. These
    // thresholds detect that collapse so it can be retried and, failing that, prevented from
    // becoming the working draft. Compared against the running high-water mark, not just the
    // immediately previous draft, so an accepted partial can never lower the bar.
    private const int    MinComparableDraftLength = 2000;
    private const double RegressionRatio          = 0.7;

    public async Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var brief = context.GetRequired(AtelierContextKeys.GroundedBrief);
        var iter  = context.GetRequired(GeefKeys.CurrentIteration);

        var (client, model, maxTokens) = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);

        var isCliProvider = ModelNameNormalizer.IsCliProvider(profile.Provider);

        string? prevDraft = null;
        string  findingList = "";
        string  userPrompt;
        string? currentDocument = null;  // null = API-provider (no document mode)

        if (iter == 1)
        {
            if (context.TryGet(AtelierContextKeys.SeedDraft, out var seedDraft) && seedDraft is not null)
            {
                if (isCliProvider)
                {
                    // Document mode: draft.md already contains the seed draft; instruction only needs context.
                    userPrompt = $"""
                        Briefing:
                        {brief}

                        Revise the draft (already in draft.md) to better fulfill the briefing. Improve quality,
                        address any weaknesses you can identify, and make the text more polished.
                        Write the COMPLETE revised document back to draft.md.
                        """;
                    currentDocument = seedDraft;
                }
                else
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
            }
            else
            {
                if (isCliProvider)
                {
                    // Document mode: draft.md is empty; instruction writes the initial document.
                    userPrompt = $"Briefing:\n{brief}\n\nWrite the complete document into draft.md according to the briefing.";
                    currentDocument = "";
                }
                else
                {
                    userPrompt = $"Briefing:\n{brief}\n\nWrite a text according to the briefing.";
                }
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
            userPrompt = BuildRevisionPrompt(brief, prevDraft, findingList, forceful: false, documentMode: isCliProvider);
            if (isCliProvider) currentDocument = prevDraft;
        }

        // In document mode the grounding/advisor context is kept separate from the steering
        // instruction so the proxy can offload it to context.md (avoids the per-argument argv
        // limit) while findings + "edit draft.md" stay prominent in the prompt. In text mode the
        // context is prepended inline as before.
        string? contextDocument = null;
        if (isCliProvider)
            contextDocument = CollectContextBlocks(context);
        else
            userPrompt = PrependContextBlocks(context, userPrompt);

        var response = await CompleteAndRecordAsync(
            client, model, userPrompt, iter, currentDocument, contextDocument, maxTokens, cancellationToken);

        // Safety net: never let a change-summary / cover-letter / abbreviated partial (a collapse far
        // below the most complete draft seen so far) become the new state. Retry once forcefully; if it
        // still collapses, keep whichever text preserves the most content so the run never regresses
        // below its best draft. The yardstick is the running high-water mark, so an accepted partial in
        // an earlier iteration cannot lower the bar for later ones.
        var resultText = response.Text;
        var tokenUsage = response.TokenUsage;
        if (prevDraft is not null)
        {
            var bestDraft = context.TryGet(AtelierContextKeys.BestDraft, out var best) && best is not null
                ? best
                : prevDraft;

            if (IsRegression(resultText, bestDraft))
            {
                var retryBase = BuildRevisionPrompt(brief, prevDraft, findingList, forceful: true, documentMode: isCliProvider);
                var retryPrompt = isCliProvider ? retryBase : PrependContextBlocks(context, retryBase);
                var retry = await CompleteAndRecordAsync(
                    client, model, retryPrompt, iter, currentDocument, contextDocument, maxTokens, cancellationToken);

                if (!IsRegression(retry.Text, bestDraft))
                {
                    resultText = retry.Text;
                    tokenUsage = retry.TokenUsage;
                }
                else
                {
                    // Both attempts collapsed — keep the longest available text (the prior full draft
                    // wins over two short changelogs), guaranteeing no regression in document state.
                    resultText = new[] { resultText, retry.Text, bestDraft }.MaxBy(t => t.Length)!;
                    tokenUsage = retry.TokenUsage;
                }
            }
        }

        var updated = context
            .Set(AtelierContextKeys.CurrentDraft, resultText)
            .Set(AtelierContextKeys.BestDraft, LongestOf(resultText, prevDraft, context))
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

    // Document-mode counterpart to PrependContextBlocks: returns the combined grounding/advisor
    // context as a standalone string (advisor first, then grounding — matching the inline order),
    // or null when neither is present. The proxy decides whether to inline it or offload to
    // context.md based on size.
    private static string? CollectContextBlocks(IRunContext context)
    {
        var parts = new List<string>(2);
        if (context.TryGet(AtelierContextKeys.AdvisorBlock, out var advisorBlock) && advisorBlock is not null)
            parts.Add(advisorBlock);
        if (context.TryGet(AtelierContextKeys.GroundingContext, out var groundingCtx) && groundingCtx is not null)
            parts.Add(groundingCtx);
        return parts.Count > 0 ? string.Join("\n\n", parts) : null;
    }

    private async Task<LlmResponse> CompleteAndRecordAsync(
        ILlmClient client, string model, string userPrompt, int iter,
        string? currentDocument, string? contextDocument, int maxTokens, CancellationToken cancellationToken)
    {
        var isDocumentMode = currentDocument is not null;
        var response = await client.CompleteAsync(new LlmRequest
        {
            Model           = model,
            SystemPrompt    = profile.SystemPrompt,
            UserPrompt      = userPrompt,
            MaxTokens       = maxTokens,
            DocumentMode    = isDocumentMode,
            Document        = isDocumentMode ? currentDocument : null,
            ContextDocument = isDocumentMode ? contextDocument : null,
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

    private static string BuildRevisionPrompt(
        string brief, string prevDraft, string findingList, bool forceful, bool documentMode = false)
    {
        string emphasis;
        if (!forceful)
            emphasis = "";
        else if (documentMode)
            // File-edit-specific retry emphasis: the previous run wrote a change-summary or partial
            // result to draft.md instead of the complete document. Stdout-mode language ("Output the
            // ENTIRE document… NOTHING else") would contradict the file-edit contract and silently
            // no-op the retry because the proxy reads draft.md, not stdout.
            emphasis = "\n\nIMPORTANT: The previous run produced a change-summary or partially "
                + "edited file instead of a complete document. Write the COMPLETE updated document "
                + "to draft.md this time — every section intact, all findings addressed, nothing omitted.";
        else
            emphasis = "\n\nIMPORTANT: Your previous response was REJECTED because it was a change-summary / "
                + "cover letter / list of edits instead of the document itself. Output the ENTIRE document "
                + "in full this time — every section, in final form — and NOTHING else.";

        if (documentMode)
        {
            // In document mode the current draft is already in draft.md — do not embed it in the
            // prompt to avoid doubling the context (and potentially exhausting the token budget for
            // very long documents). The CLI agent reads and writes the file directly.
            return $$"""
                Briefing:
                {{brief}}

                Reviewer findings — resolve each one with a concrete, visible change in the document:
                {{findingList}}

                The document is in draft.md. Read it, apply the revisions, and write the complete
                updated document back to draft.md. Make every change visible and specific.{{emphasis}}
                """;
        }

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

    private static bool IsRegression(string newText, string baseline)
        => baseline.Length >= MinComparableDraftLength
           && newText.Length < baseline.Length * RegressionRatio;

    // The new high-water mark: the longest of the freshly chosen draft, the previous draft, and the
    // best draft tracked so far. Keeps the yardstick monotonic across iterations.
    private static string LongestOf(string resultText, string? prevDraft, IRunContext context)
    {
        var best = resultText;
        if (prevDraft is not null && prevDraft.Length > best.Length)
            best = prevDraft;
        if (context.TryGet(AtelierContextKeys.BestDraft, out var tracked) && tracked is not null
            && tracked.Length > best.Length)
            best = tracked;
        return best;
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
