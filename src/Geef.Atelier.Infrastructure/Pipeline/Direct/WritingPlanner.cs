using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>
/// The planning call of "Extra careful" with an outline (D-087): one call of the writer's model, before the first writing pass, that splits
/// the text into sections with word budgets (<see cref="WritingPlan"/>). It sees the request and the files exactly as the writer does
/// (document mode: the plan goes into an empty <c>draft.md</c>, the files are the background document, no failover). Its cost is recorded as
/// actor <see cref="ActorName"/>. A call that fails (also on a timeout or a transient error of the gateway) or gives no readable plan is
/// not repeated: the text is then written without an outline
/// and the run gets the warning <see cref="NoPlanWarning"/>, so that nobody believes a plan was followed.
/// </summary>
internal sealed class WritingPlanner(
    ExecutorProfile profile,
    ILlmClientResolver resolver,
    IPricingCatalog? pricingCatalog,
    ICostAccumulator? costAccumulator,
    IRunWarningSink warningSink,
    Guid runId,
    ILogger logger)
{
    /// <summary>Actor name of the planning call in the cost records.</summary>
    public const string ActorName = "direct-outline";

    /// <summary>Warning of a run whose text was written without an outline because the planning call gave none.</summary>
    public const string NoPlanWarning = "The text could not be planned section by section first, so it was written in one go.";

    /// <summary>
    /// Plans the text of <paramref name="brief"/> with the files of <paramref name="bundle"/>, fitted to <paramref name="targetWords"/> when the
    /// code read a length in words from the request (<see cref="WritingPlanRules.FitTo"/>). <c>null</c> when no plan came back; then the
    /// warning is stored. Cancellation is passed on.
    /// </summary>
    public async Task<WritingPlan?> PlanAsync(string brief, OrderSourceBundle bundle, int? targetWords, CancellationToken cancellationToken)
    {
        var (client, model, maxTokens) = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);
        var documentMode = ModelNameNormalizer.IsCliProvider(profile.Provider);
        var request = new LlmRequest
        {
            Model = model,
            SystemPrompt = WritingPlanPrompts.SystemPrompt,
            UserPrompt = WritingPlanPrompts.User(brief, bundle, documentMode, targetWords),
            MaxTokens = maxTokens,
            DocumentMode = documentMode,
            Document = documentMode ? string.Empty : null,
            ContextDocument = documentMode && bundle.ContextDocument.Length > 0 ? bundle.ContextDocument : null,
            DisableFailover = documentMode,
        };

        WritingPlan? plan = null;
        try
        {
            var response = await LlmResilience.ExecuteAsync(ct => CompleteAsync(client, request, ct), cancellationToken, maxAttempts: 1);
            Record(request.Model, response);
            plan = WritingPlanRules.Parse(response.Text)?.FitTo(targetWords);
            if (plan is null)
                logger.LogWarning("Run {RunId}: the planning call gave no readable outline ({Length} characters).", runId, response.Text?.Length ?? 0);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Run {RunId}: the planning call failed: {Error}", runId, ex.ToRedactedLogText());
        }

        if (plan is null)
        {
            await warningSink.ReportAsync(runId, RunWarningSources.Llm, DirectWarningCodes.NoWritingPlan, NoPlanWarning, 1, cancellationToken);
            return null;
        }

        logger.LogInformation("Run {RunId}: outline with {Sections} sections and {Words} words.", runId, plan.Sections.Count, plan.TotalWords);
        return plan;
    }

    private void Record(string model, LlmResponse response)
    {
        if (costAccumulator is null)
            return;
        var costEur = pricingCatalog?.CalculateCostEur(
            model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, profile.Provider,
            cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0);
        costAccumulator.RecordActorCost(
            1, ActorType.Executor, ActorName, model,
            response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, costEur,
            providerName: profile.Provider,
            cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0,
            reasoningTokens: response.TokenUsage.ReasoningTokens ?? 0);
    }

    private static async Task<LlmResponse> CompleteAsync(ILlmClient client, LlmRequest request, CancellationToken ct)
    {
        try
        {
            return await client.CompleteAsync(request, ct);
        }
        catch (HttpRequestException ex) when (ex.ErrorCode() is LlmApiError.EmptyResultCode or LlmApiError.BackendUnavailableCode)
        {
            throw new PlanSignalException(ex.ErrorCode()!);
        }
    }

    /// <summary>A cli-proxy answer that must not be retried by <see cref="LlmResilience"/>; it carries no inner exception.</summary>
    private sealed class PlanSignalException(string code) : Exception(code);
}
