using System.Net;

namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>
/// Rules for the retry of the shared "llm" <c>HttpClient</c>. A request sent with failover switched off
/// (<see cref="LlmRequest.DisableFailover"/>) belongs to a caller that handles the cli-proxy answers
/// <see cref="LlmApiError.EmptyResultCode"/> (502) and <see cref="LlmApiError.BackendUnavailableCode"/> (503) itself:
/// one retry with a hint for an empty result, an immediate plain message when the CLI is unavailable. Retrying these
/// answers in the <c>HttpClient</c> would hide the first one and wait for the <c>Retry-After</c> of the second one.
/// </summary>
internal static class LlmHttpRetry
{
    /// <summary>
    /// True when <paramref name="response"/> is a 502 or 503 answer to a request with failover switched off; the
    /// <c>HttpClient</c> must not retry it. The request is read from <see cref="HttpResponseMessage.RequestMessage"/>,
    /// which the primary handler sets.
    /// </summary>
    public static bool IsAnswerForCallerWithoutFailover(this HttpResponseMessage? response)
    {
        if (response is null || response.StatusCode is not (HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable))
            return false;

        var sent = response.RequestMessage;
        return sent is not null
               && sent.Headers.TryGetValues(OpenAiCompatibleClient.FailoverHeader, out var values)
               && values.Contains(OpenAiCompatibleClient.FailoverOff, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when <paramref name="request"/> carries a time limit for the CLI process (<see cref="OpenAiCompatibleClient.TimeoutHeader"/>).
    /// Such a caller decides itself what follows a failed answer (the fact check: one call of another model, never a second call of
    /// the same one); a retry of an answered request in the <c>HttpClient</c> would start the CLI once more.
    /// </summary>
    public static bool HasCliTimeLimit(this HttpRequestMessage? request) =>
        request is not null && request.Headers.Contains(OpenAiCompatibleClient.TimeoutHeader);

    /// <summary>
    /// Whether the <c>HttpClient</c> may retry an attempt that ended with <paramref name="response"/>: never an answer to a request
    /// with a CLI time limit (so the cli-proxy's <c>504 cli_timeout</c>, its answer to a run that hung, is never retried), never
    /// the answers of <see cref="IsAnswerForCallerWithoutFailover"/>; otherwise the usual transient rules decide.
    /// Without an answer (<paramref name="response"/> is null, after an exception) the result is true even for a request with a CLI
    /// time limit, because the request is only known from the answer: if, for example, the connection breaks after the request was
    /// sent, the transient rules may retry it and the CLI may start again within the caller's own time limit.
    /// A call that runs into the caller's own limit is cancelled by the caller's token and never retried.
    /// </summary>
    public static bool MayRetry(HttpResponseMessage? response) =>
        !(response?.RequestMessage).HasCliTimeLimit() && !response.IsAnswerForCallerWithoutFailover();
}
