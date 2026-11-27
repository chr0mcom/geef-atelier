using System.Diagnostics;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

/// <summary>
/// <see cref="IFactChecker"/> over the gateway: structured output (<see cref="FactCheckSchema"/>) in print mode, proxy failover
/// limited to the same model, reasoning effort from <see cref="DirectOptions"/>, the time limit per call from the caller, costs recorded per call. One
/// attempt per call: the fallback to another model is the caller's decision (<see cref="IsUnavailable"/>). The time limit goes to
/// the cli-proxy as well (<see cref="ProxyTimeoutSeconds"/>, <see cref="ProxyTimeoutMargin"/> below the own limit), so that the proxy
/// ends the CLI process and its child processes first instead of letting it run on after the call was given up; the own
/// <see cref="CancellationTokenSource"/> stays as the safety net.
/// </summary>
internal sealed class LlmFactChecker(ILlmClientResolver resolver, DirectOptions options, IPricingCatalog? pricing, ICostAccumulator? costs, ILogger<LlmFactChecker> logger) : IFactChecker
{
    /// <summary>
    /// Error code of a call that ran into its time limit (<see cref="DirectOptions.FactCheckTimeout"/> or
    /// <see cref="DirectOptions.FactCheckFallbackTimeout"/>), whether the own limit or the cli-proxy's (<see cref="LlmApiError.CliTimeoutCode"/>) ended it.
    /// </summary>
    public const string TimeoutCode = "timeout";

    /// <summary>Error code of an answer without JSON (a refusal).</summary>
    public const string NoJsonCode = "no_json";

    /// <summary>Error code of JSON without the expected list.</summary>
    public const string UnusableCode = "unusable_json";

    /// <summary>Error code of a failed HTTP call without status.</summary>
    public const string HttpCode = "http";

    /// <summary>Error code of any other failure.</summary>
    public const string FailureCode = "error";

    /// <summary>Error code the cli-proxy sends with HTTP 429.</summary>
    public const string RateLimitCode = "rate_limit_exceeded";

    /// <summary>How much earlier than the own limit the cli-proxy is asked to end the CLI process.</summary>
    public static readonly TimeSpan ProxyTimeoutMargin = TimeSpan.FromSeconds(5);

    /// <summary>The value of <c>x-cli-proxy-timeout-seconds</c> for a call limited to <paramref name="timeout"/>: <see cref="ProxyTimeoutMargin"/> earlier, at least 1 s.</summary>
    public static int ProxyTimeoutSeconds(TimeSpan timeout) =>
        (int)Math.Max(1, Math.Floor((timeout - ProxyTimeoutMargin).TotalSeconds));

    private static readonly LlmTool CheckTool = new()
    {
        Name = FactCheckSchema.CheckName,
        Description = "Report the facts in the text that the material does not back, broken instructions and problems of the material.",
        InputSchema = FactCheckSchema.Check,
    };

    /// <summary>
    /// Whether <paramref name="errorCode"/> says the model could not be reached (unavailable, limit, gateway failure or time
    /// limit), so that one more attempt with another model makes sense. An unreadable answer or a refusal is not such a case.
    /// </summary>
    public static bool IsUnavailable(string? errorCode) =>
        errorCode is TimeoutCode or LlmApiError.BackendUnavailableCode or RateLimitCode or HttpCode or "http_429" or "http_502" or "http_503" or "http_504";

    /// <inheritdoc/>
    public async Task<FactCheckCall> CheckAsync(FactMaterial material, string draft, ReviewerProfile profile, TimeSpan timeout, int iteration, CancellationToken ct)
    {
        var call = await CallAsync(CheckTool, FactCheckPrompts.System, FactCheckPrompts.User(material, draft), profile, iteration,
            FactCheckOutputParser.ParseCheckCounted, timeout, ct);
        return new FactCheckCall(call.Value?.Findings, call.RawJson, call.DurationMs, call.Model, call.ErrorCode, call.Value?.Dropped ?? 0);
    }

    private sealed record Outcome<T>(T? Value, string? RawJson, int DurationMs, string Model, string? ErrorCode) where T : class;

    private async Task<Outcome<T>> CallAsync<T>(LlmTool tool, string system, string user, ReviewerProfile profile, int iteration,
        Func<string?, T?> parse, TimeSpan limit, CancellationToken ct) where T : class
    {
        var watch = Stopwatch.StartNew();
        using var timeout = new CancellationTokenSource(limit);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        var model = profile.Model;

        try
        {
            var (client, resolvedModel, _) = resolver.ForProfile(profile.Provider, profile.Model, options.FactCheckMaxTokens);
            model = resolvedModel;
            var (tools, toolChoice, responseFormat) = StructuredOutput.Build(tool, resolver.SupportsStructuredOutputs(profile.Provider));

            var response = await client.CompleteAsync(new LlmRequest
            {
                Model = resolvedModel,
                SystemPrompt = system,
                UserPrompt = user,
                MaxTokens = options.FactCheckMaxTokens,
                Tools = tools,
                ToolChoice = toolChoice,
                ResponseFormat = responseFormat,
                ReasoningEffort = string.IsNullOrWhiteSpace(options.FactCheckReasoningEffort) ? null : options.FactCheckReasoningEffort,
                DisableFailover = true,
                ProxyTimeoutSeconds = ProxyTimeoutSeconds(limit),
            }, linked.Token);
            RecordCost(response, profile, resolvedModel, iteration);

            var json = StructuredOutput.ExtractJson(response);
            if (json is null)
                return Done<T>(tool, null, null, model, watch, NoJsonCode, $"finish_reason={response.FinishReason}");

            var value = parse(json);
            return value is null
                ? Done<T>(tool, null, json, model, watch, UnusableCode, "expected list missing")
                : Done(tool, value, json, model, watch, null, null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Done<T>(tool, null, null, model, watch, TimeoutCode, $"no answer within {limit.TotalSeconds:0} s");
        }
        catch (HttpRequestException ex) when (ex.ErrorCode() == LlmApiError.CliTimeoutCode || ex.StatusCode == System.Net.HttpStatusCode.GatewayTimeout)
        {
            return Done<T>(tool, null, null, model, watch, TimeoutCode, $"the gateway ended the call at its limit of {ProxyTimeoutSeconds(limit)} s");
        }
        catch (HttpRequestException ex)
        {
            var code = ex.ErrorCode() ?? (ex.StatusCode is { } status ? $"http_{(int)status}" : HttpCode);
            return Done<T>(tool, null, null, model, watch, code, ex.ToRedactedLogText());
        }
        catch (Exception ex)
        {
            return Done<T>(tool, null, null, model, watch, FailureCode, ex.ToRedactedLogText());
        }
    }

    private void RecordCost(LlmResponse response, ReviewerProfile profile, string model, int iteration)
    {
        if (costs is null)
            return;
        var usage = response.TokenUsage;
        var costEur = pricing?.CalculateCostEur(model, usage.InputTokens, usage.OutputTokens, profile.Provider, cachedInputTokens: usage.CachedInputTokens ?? 0);
        costs.RecordActorCost(iteration, ActorType.Reviewer, profile.Name, model, usage.InputTokens, usage.OutputTokens, costEur,
            providerName: profile.Provider, cachedInputTokens: usage.CachedInputTokens ?? 0, reasoningTokens: usage.ReasoningTokens ?? 0);
    }

    private Outcome<T> Done<T>(LlmTool tool, T? value, string? rawJson, string model, Stopwatch watch, string? errorCode, string? detail) where T : class
    {
        var duration = (int)Math.Min(int.MaxValue, watch.Elapsed.TotalMilliseconds);
        if (errorCode is null)
            logger.LogInformation("Fact check {Call} answered in {DurationMs} ms ({Model}).", tool.Name, duration, model);
        else
            logger.LogWarning("Fact check {Call} failed after {DurationMs} ms ({Model}, {Code}): {Detail}", tool.Name, duration, model, errorCode, detail);
        return new Outcome<T>(value, rawJson, duration, model, errorCode);
    }
}
