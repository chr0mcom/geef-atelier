using System.Net;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>
/// One structured call on the model of <see cref="ModelSlot.GroundingRefiner"/> with the schema <see cref="DossierSchema.Name"/>,
/// <c>reasoning_effort</c> <see cref="ReasoningEffort"/>, without failover and with its own hard time limit
/// (<see cref="ResearchOptions.CondenseTimeout"/>, gateway limit <see cref="ProxyTimeoutSeconds"/>). No second attempt, no other model.
/// </summary>
/// <remarks>
/// The request carries the gateway time limit, so the shared <c>HttpClient</c> never repeats an answered request (a 504 of the gateway
/// included, <see cref="LlmHttpRetry.MayRetry"/>). The log names model, duration and counts only, never topics, pages or passages.
/// </remarks>
internal sealed class LlmDossierCondenser(
    IModelAssignmentService models,
    ILlmClientResolver resolver,
    IOptions<ResearchOptions> options,
    ILogger<LlmDossierCondenser> logger,
    TimeProvider time) : IDossierCondenser
{
    public const string ReasoningEffort = "low";
    public const int ProxyTimeoutSeconds = 55;

    /// <summary>Token limit of the answer: 40 passages of up to 600 characters with room to spare.</summary>
    public const int MaxTokens = 16_384;

    /// <summary>Error code of a call that ran into its time limit or got the gateway's 504.</summary>
    public const string TimeoutCode = "timeout";

    /// <summary>Error code of every other failure: no answer, no JSON, unusable JSON, an error status.</summary>
    public const string FailedCode = "condense_failed";

    public async Task<CondenseOutcome> CondenseAsync(IReadOnlyList<string> topics, IReadOnlyList<ResearchPage> pages, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(topics);
        ArgumentNullException.ThrowIfNull(pages);

        var started = time.GetTimestamp();
        using var timeout = new CancellationTokenSource(options.Value.CondenseTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        string? model = null;

        try
        {
            var assignment = await models.AssignAsync(ModelSlot.GroundingRefiner, ModelDomain.General, null, linked.Token);
            if (assignment.IsFallback)
                logger.LogWarning("Research passages: model assignment fell back to {Model}: {Warning}", assignment.Candidate.Model, assignment.Warning);

            var (client, resolvedModel, _) = resolver.ForProfile(assignment.Candidate.Provider, assignment.Candidate.Model, MaxTokens);
            model = resolvedModel;
            var (tools, toolChoice, responseFormat) = StructuredOutput.Build(
                DossierSchema.Tool, resolver.SupportsStructuredOutputs(assignment.Candidate.Provider));

            var response = await client.CompleteAsync(new LlmRequest
            {
                Model = resolvedModel,
                SystemPrompt = DossierPrompts.System,
                UserPrompt = DossierPrompts.User(topics, pages),
                MaxTokens = MaxTokens,
                Tools = tools,
                ToolChoice = toolChoice,
                ResponseFormat = responseFormat,
                ReasoningEffort = ReasoningEffort,
                DisableFailover = true,
                ProxyTimeoutSeconds = ProxyTimeoutSeconds,
            }, linked.Token);

            var json = StructuredOutput.ExtractJson(response);
            if (json is null)
                return Failed(FailedCode, model, started, $"no JSON, finish_reason={response.FinishReason}");

            var parsed = DossierOutputParser.Parse(json, pages.Select(p => p.Id).ToList(), topics.Count);
            if (parsed is null)
                return Failed(FailedCode, model, started, "unusable JSON");

            var duration = Elapsed(started);
            logger.LogInformation("Research passages: {Passages} proposed, {Dropped} dropped by the parser in {DurationMs} ms ({Model}).",
                parsed.Passages.Count, parsed.Dropped.Values.Sum(), duration, model);
            return new CondenseOutcome(parsed.Passages, parsed.Dropped, model, null, duration);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Failed(TimeoutCode, model, started, $"no answer within {options.Value.CondenseTimeout.TotalSeconds:0} s");
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.GatewayTimeout)
        {
            return Failed(TimeoutCode, model, started, "gateway time limit (504)");
        }
        catch (HttpRequestException ex)
        {
            return Failed(FailedCode, model, started, ex.StatusCode is { } status ? $"HTTP {(int)status}" : "no answer");
        }
        catch (Exception ex)
        {
            return Failed(FailedCode, model, started, ex.GetType().Name);
        }
    }

    private CondenseOutcome Failed(string code, string? model, long started, string detail)
    {
        var duration = Elapsed(started);
        logger.LogWarning("Research passages failed after {DurationMs} ms ({Model}, {Code}): {Detail}", duration, model, code, detail);
        return new CondenseOutcome([], new Dictionary<ResearchDropReason, int>(), model, code, duration);
    }

    private int Elapsed(long started) => (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds);
}
