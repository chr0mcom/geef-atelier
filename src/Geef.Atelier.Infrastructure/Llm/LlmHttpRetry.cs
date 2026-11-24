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
}
