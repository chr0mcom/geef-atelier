using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using FindingSeverity = Geef.Sdk.Results.FindingSeverity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>
/// Writer of the direct path. First pass: the request plus the order's files as background document; a CLI provider
/// writes into <c>draft.md</c> (seeded with a template to revise when there is one). Correction pass: only the
/// blocking findings of the automatic checks of the previous pass, never failures of the checks themselves.
/// An empty or unchanged answer is never accepted: the call is repeated once; if the first pass still has no text the
/// run stops with <see cref="DirectPathException.WriterReturnedNoText"/>, a correction pass keeps the previous text.
/// In document mode the call asks the cli-proxy not to fail over (another CLI cannot edit the same files); the
/// proxy's <c>empty_result</c> answer counts as no text, <c>backend_unavailable</c> stops the run at once with
/// <see cref="DirectPathException.WritingServiceBusy"/>.
/// After the first pass the input tokens are compared with the size of the files (<see cref="ReadEvidenceCheck"/>);
/// too few become the warning <see cref="PartialReadWarning"/>.
/// </summary>
internal sealed class DirectWriterExecutor(
    ExecutorProfile profile,
    ILlmClientResolver resolver,
    IPricingCatalog? pricingCatalog = null,
    ICostAccumulator? costAccumulator = null,
    IRunWarningSink? warningSink = null,
    Guid runId = default,
    ILogger? logger = null,
    IDraftOutputSplitter? splitter = null) : IExecutionStep
{
    /// <summary>Warning shown when the writer most likely searched the files instead of reading them.</summary>
    public const string PartialReadWarning = "Some of your files may not have been read in full.";

    /// <summary>Characters of the files sampled to guess their language for the token estimate.</summary>
    private const int LanguageSampleChars = 20_000;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly IDraftOutputSplitter _splitter = splitter ?? IdentityDraftOutputSplitter.Instance;

    public async Task<ExecutionResult> RunAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var brief = context.GetRequired(AtelierContextKeys.GroundedBrief);
        var iteration = context.GetRequired(GeefKeys.CurrentIteration);
        var bundle = context.TryGet(AtelierContextKeys.OrderSources, out var sources) && sources is not null
            ? sources
            : OrderSourceBundle.Empty;

        var (client, model, maxTokens) = resolver.ForProfile(profile.Provider, profile.Model, profile.MaxTokens);
        var documentMode = ModelNameNormalizer.IsCliProvider(profile.Provider);
        var systemPrompt = DirectWriterPrompts.SystemPrompt(profile.SystemPrompt, documentMode);

        string userPrompt;
        string baseline;
        string? previous = null;
        if (iteration == 1)
        {
            var seed = context.TryGet(AtelierContextKeys.SeedDraft, out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
            userPrompt = DirectWriterPrompts.FirstPass(brief, bundle, documentMode, seed);
            baseline = seed ?? string.Empty;
        }
        else
        {
            previous = context.GetRequired(AtelierContextKeys.CurrentDraft);
            userPrompt = DirectWriterPrompts.Correction(brief, bundle, BlockingProblems(context), documentMode, previous);
            baseline = previous;
        }

        var request = new LlmRequest
        {
            Model = model,
            SystemPrompt = systemPrompt,
            UserPrompt = userPrompt,
            MaxTokens = maxTokens,
            DocumentMode = documentMode,
            Document = documentMode ? baseline : null,
            ContextDocument = documentMode && bundle.ContextDocument.Length > 0 ? bundle.ContextDocument : null,
            DisableFailover = documentMode,
        };

        var response = await CompleteAndRecordAsync(client, request, iteration, cancellationToken);
        var text = response is null ? string.Empty : _splitter.Split(response.Text).Text;
        var notes = new List<string>();

        if (IsNoText(text, baseline))
        {
            LogNoText(response, model, iteration, attempt: 1);
            response = await CompleteAndRecordAsync(
                client, request with { UserPrompt = userPrompt + DirectWriterPrompts.RetryReminder(documentMode) }, iteration, cancellationToken);
            text = response is null ? string.Empty : _splitter.Split(response.Text).Text;

            if (IsNoText(text, baseline))
            {
                LogNoText(response, model, iteration, attempt: 2);
                if (previous is null)
                    throw new DirectPathException(DirectPathException.WriterReturnedNoText);
                text = previous;
                notes.Add("correction returned no usable text twice; previous text kept");
            }
        }

        var usage = response?.TokenUsage ?? new LlmTokenUsage { InputTokens = 0, OutputTokens = 0 };
        var updated = context
            .Set(AtelierContextKeys.CurrentDraft, text)
            .Set(AtelierContextKeys.TokenUsage, usage);

        if (iteration == 1 && documentMode && bundle.Mode == SourceUsageMode.FullText && bundle.ContextDocument.Length > 0)
        {
            var evidence = ReadEvidenceCheck.Evaluate(
                bundle.ContextDocument.Length,
                usage.InputTokens,
                ReadEvidenceCheck.CharactersPerTokenFor(LanguageSample(bundle)));
            if (evidence is not null)
            {
                updated = updated.Set(AtelierContextKeys.ReadEvidence, evidence);
                notes.Add($"read-evidence expected={evidence.ExpectedInputTokens} actual={evidence.ActualInputTokens} share={evidence.ReadShare}");
                if (!evidence.ReadInFull && warningSink is not null)
                    await warningSink.ReportAsync(runId, RunWarningSources.Llm, DirectWarningCodes.PartialRead, PartialReadWarning, iteration, cancellationToken);
            }
        }

        notes.Add($"tokens_in={usage.InputTokens} tokens_out={usage.OutputTokens}");
        return new ExecutionResult { UpdatedContext = updated, Notes = notes };
    }

    /// <summary>
    /// Messages of the blocking findings of the automatic checks in the previous pass. Findings of other reviewers and
    /// reports about checks that could not run are never passed on.
    /// </summary>
    internal static IReadOnlyList<string> BlockingProblems(IRunContext context)
    {
        if (!context.TryGet(GeefKeys.IterationHistory, out var history) || history is null || history.Records.Count == 0)
            return [];

        return history.Records[^1].EvaluationResult.AllFindings
            .Where(f => f.Severity is FindingSeverity.Error or FindingSeverity.Critical
                        && f.Category == DeterministicChecksReviewer.FindingCategory)
            .Select(f => f.Message)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>True when <paramref name="text"/> is blank or, ignoring surrounding white space, equals what the writer was given.</summary>
    internal static bool IsNoText(string? text, string baseline) =>
        string.IsNullOrWhiteSpace(text)
        || (baseline.Length > 0 && string.Equals(text.Trim(), baseline.Trim(), StringComparison.Ordinal));

    private static string LanguageSample(OrderSourceBundle bundle)
    {
        var sample = string.Join('\n', bundle.Sources.Select(s => s.Text));
        return sample.Length <= LanguageSampleChars ? sample : sample[..LanguageSampleChars];
    }

    private void LogNoText(LlmResponse? response, string model, int iteration, int attempt) =>
        _logger.LogWarning(
            "Direct writer returned no usable text (provider {Provider}, model {Model}, iteration {Iteration}, attempt {Attempt}, finish reason {FinishReason}, {Length} characters, tokens in {InputTokens}, out {OutputTokens}).",
            profile.Provider, model, iteration, attempt, response?.FinishReason.RedactSecrets() ?? LlmApiError.EmptyResultCode,
            response?.Text?.Length ?? 0, response?.TokenUsage.InputTokens ?? 0, response?.TokenUsage.OutputTokens ?? 0);

    /// <summary>Calls the writer; <c>null</c> when the cli-proxy reported an empty result.</summary>
    private async Task<LlmResponse?> CompleteAndRecordAsync(ILlmClient client, LlmRequest request, int iteration, CancellationToken cancellationToken)
    {
        LlmResponse response;
        try
        {
            response = await LlmResilience.ExecuteAsync(ct => CompleteOrSignalAsync(client, request, ct), cancellationToken);
        }
        catch (ProxySignalException signal) when (signal.Code == LlmApiError.EmptyResultCode)
        {
            _logger.LogWarning("Direct writer: the cli-proxy reported an empty result (iteration {Iteration}): {Detail}", iteration, signal.Detail);
            return null;
        }
        catch (ProxySignalException signal)
        {
            _logger.LogWarning("Direct writer: the writing service is unavailable and failover is off (iteration {Iteration}): {Detail}", iteration, signal.Detail);
            throw new DirectPathException(DirectPathException.WritingServiceBusy);
        }

        if (costAccumulator is not null)
        {
            var costEur = pricingCatalog?.CalculateCostEur(
                request.Model, response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, profile.Provider,
                cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0);
            costAccumulator.RecordActorCost(
                iteration, ActorType.Executor, profile.Name, request.Model,
                response.TokenUsage.InputTokens, response.TokenUsage.OutputTokens, costEur,
                providerName: profile.Provider,
                cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0,
                reasoningTokens: response.TokenUsage.ReasoningTokens ?? 0);
        }

        return response;
    }

    private static async Task<LlmResponse> CompleteOrSignalAsync(ILlmClient client, LlmRequest request, CancellationToken ct)
    {
        try
        {
            return await client.CompleteAsync(request, ct);
        }
        catch (HttpRequestException ex) when (ex.ErrorCode() is LlmApiError.EmptyResultCode or LlmApiError.BackendUnavailableCode)
        {
            throw new ProxySignalException(ex.ErrorCode()!, ex.Message.RedactSecrets());
        }
    }

    /// <summary>
    /// A cli-proxy answer that must not be retried by <see cref="LlmResilience"/>: it carries no inner exception, so it
    /// never counts as transient. Handled in <see cref="CompleteAndRecordAsync"/>.
    /// </summary>
    private sealed class ProxySignalException(string code, string detail) : Exception(code)
    {
        public string Code { get; } = code;
        public string Detail { get; } = detail;
    }
}
