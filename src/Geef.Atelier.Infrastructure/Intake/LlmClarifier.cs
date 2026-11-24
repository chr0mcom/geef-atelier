using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Intake;

/// <summary>
/// The clarifier: one structured call on the model of <see cref="ModelSlot.Intake"/> with <c>response_format</c> json_schema
/// (or the forced tool where the provider has no structured outputs), <c>reasoning_effort</c> from the options and its own
/// hard time limit. No second attempt: the gateway already repeats an invalid answer once.
/// </summary>
internal sealed class LlmClarifier(
    IModelAssignmentService models,
    ILlmClientResolver resolver,
    IOptions<IntakeOptions> options,
    ILogger<LlmClarifier> logger,
    TimeProvider time) : IClarifier
{
    /// <summary>Error code of a call that ran into <see cref="IntakeOptions.Timeout"/>.</summary>
    public const string TimeoutCode = "timeout";

    /// <summary>Error code of an answer without JSON (a refusal).</summary>
    public const string NoJsonCode = "no_json";

    /// <summary>Error code of JSON that misses understanding, language or gaps.</summary>
    public const string UnusableCode = "unusable_json";

    /// <summary>Error code of a failed HTTP call without status.</summary>
    public const string HttpCode = "http";

    /// <summary>Error code of any other failure.</summary>
    public const string ErrorCode = "error";

    public async Task<ClarifierCallResult> ClarifyAsync(ClarifierRequest request, CancellationToken ct)
    {
        var settings = options.Value;
        var started = time.GetTimestamp();
        using var timeout = new CancellationTokenSource(settings.Timeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        string? model = null;

        try
        {
            var assignment = await models.AssignAsync(ModelSlot.Intake, ModelDomain.General, null, linked.Token);
            if (assignment.IsFallback)
                logger.LogWarning("Clarifier: model assignment fell back to {Model}: {Warning}", assignment.Candidate.Model, assignment.Warning);

            var (client, resolvedModel, _) = resolver.ForProfile(assignment.Candidate.Provider, assignment.Candidate.Model, settings.MaxTokens);
            model = resolvedModel;
            var (tools, toolChoice, responseFormat) = StructuredOutput.Build(
                ClarifierSchema.Tool, resolver.SupportsStructuredOutputs(assignment.Candidate.Provider));

            var response = await client.CompleteAsync(new LlmRequest
            {
                Model = resolvedModel,
                SystemPrompt = ClarifierPrompts.System,
                UserPrompt = ClarifierPrompts.User(request, settings.ExcerptHeadChars, settings.ExcerptTailChars),
                MaxTokens = settings.MaxTokens,
                Tools = tools,
                ToolChoice = toolChoice,
                ResponseFormat = responseFormat,
                ReasoningEffort = settings.ReasoningEffort,
                DisableFailover = !settings.AllowFailover,
            }, linked.Token);

            var json = StructuredOutput.ExtractJson(response);
            if (json is null)
                return Result(IntakeRecordStatus.Invalid, null, model, started, NoJsonCode, $"finish_reason={response.FinishReason}");

            var output = ClarifierOutputParser.Parse(json);
            return output is null
                ? Result(IntakeRecordStatus.Invalid, null, model, started, UnusableCode, "required fields missing", json)
                : Result(IntakeRecordStatus.Ok, output, model, started, null, null, json);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return Result(IntakeRecordStatus.TimedOut, null, model, started, TimeoutCode, $"no answer within {settings.Timeout.TotalSeconds:0} s");
        }
        catch (HttpRequestException ex)
        {
            var code = ex.StatusCode is { } status ? $"http_{(int)status}" : HttpCode;
            return Result(IntakeRecordStatus.Failed, null, model, started, code, ex.ToRedactedLogText());
        }
        catch (Exception ex)
        {
            return Result(IntakeRecordStatus.Failed, null, model, started, ErrorCode, ex.ToRedactedLogText());
        }
    }

    private ClarifierCallResult Result(
        IntakeRecordStatus status, ClarifierOutput? output, string? model, long started, string? errorCode, string? detail, string? rawJson = null)
    {
        var duration = (int)Math.Min(int.MaxValue, time.GetElapsedTime(started).TotalMilliseconds);
        if (status == IntakeRecordStatus.Ok)
            logger.LogInformation("Clarifier answered in {DurationMs} ms with {Gaps} gaps ({Model}).", duration, output!.Gaps.Count, model);
        else
            logger.LogWarning("Clarifier {Status} after {DurationMs} ms ({Model}, {Code}): {Detail}", status, duration, model, errorCode, detail);
        return new ClarifierCallResult(status, output, model, duration, errorCode, rawJson);
    }
}
