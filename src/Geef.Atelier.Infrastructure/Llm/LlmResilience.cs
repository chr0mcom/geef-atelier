namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>
/// Application-level transient-failure resilience for a single LLM actor call (executor, reviewer,
/// advisor, grounding refiner). The shared "llm" <c>HttpClient</c> already retries fast transient
/// HTTP errors a couple of times; this layer adds a patient, per-call retry so that a brief provider
/// outage during one actor's turn does not bubble up and abort the whole multi-iteration run.
///
/// Permanent errors (bad request, auth, forbidden, not-found) are never retried — retrying cannot
/// fix them, so they surface immediately with a clear, actionable message. Genuine run cancellation
/// is never retried either; only the caller's own token is honoured for that, the per-request
/// timeout token of the HttpClient is treated as a transient failure.
/// </summary>
internal static class LlmResilience
{
    /// <summary>Total attempts (initial call + retries) before a transient failure is surfaced.</summary>
    public const int DefaultMaxAttempts = 4;

    private static readonly TimeSpan DefaultBaseDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DefaultMaxDelay  = TimeSpan.FromSeconds(8);
    private const double DefaultBackoffFactor = 2.0;

    /// <summary>
    /// Invokes <paramref name="action"/>, retrying transient failures with exponential backoff up to
    /// <paramref name="maxAttempts"/> times. Non-transient failures, and the final failed attempt,
    /// surface the original exception unchanged so the caller's error handling and message
    /// classification keep working. Cancellation through <paramref name="cancellationToken"/> is
    /// never retried.
    /// </summary>
    /// <param name="action">The LLM call to run; receives the (possibly already cancelled) token.</param>
    /// <param name="cancellationToken">Caller token — its cancellation aborts the retry loop.</param>
    /// <param name="maxAttempts">Maximum number of attempts including the first.</param>
    /// <param name="baseDelay">Backoff base; <see cref="TimeSpan.Zero"/> disables waiting (tests).</param>
    /// <param name="onRetry">Optional callback invoked before each backoff wait (attempt, error, delay).</param>
    public static async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken,
        int maxAttempts = DefaultMaxAttempts,
        TimeSpan? baseDelay = null,
        Action<int, Exception, TimeSpan>? onRetry = null)
    {
        var delayBase = baseDelay ?? DefaultBaseDelay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < maxAttempts
                                       && !cancellationToken.IsCancellationRequested
                                       && IsTransient(ex))
            {
                var delay = BackoffDelay(attempt, delayBase);
                onRetry?.Invoke(attempt, ex, delay);
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// True when retrying the call could plausibly succeed: network failures, request timeouts,
    /// rate-limit (429) and server (5xx) responses. Client errors that indicate a permanent
    /// misconfiguration (any other 4xx, e.g. 400/401/403/404) are not transient. The whole
    /// inner-exception chain is inspected because both the HttpClient stack and the Geef SDK wrap the
    /// original error.
    /// </summary>
    public static bool IsTransient(Exception exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case HttpRequestException { StatusCode: { } status }:
                    var code = (int)status;
                    if (code == 408 || code == 429 || code >= 500) return true;
                    if (code >= 400) return false;
                    break;
                case HttpRequestException:        // no status code: DNS, socket reset, connection refused
                    return true;
                case TaskCanceledException:       // HttpClient / Polly request timeout (caller-cancel is filtered out by ExecuteAsync)
                case TimeoutException:
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Short, non-sensitive description of a failure for user-facing findings and log lines.
    /// Never includes API keys, provider URLs, or stack traces.
    /// </summary>
    public static string Describe(Exception exception)
    {
        for (Exception? ex = exception; ex is not null; ex = ex.InnerException)
        {
            switch (ex)
            {
                case HttpRequestException { StatusCode: { } status }:
                    return $"provider HTTP {(int)status}";
                case HttpRequestException:
                    return "provider connection error";
                case TaskCanceledException:
                case TimeoutException:
                    return "provider timeout";
            }
        }
        return "provider error";
    }

    private static TimeSpan BackoffDelay(int attempt, TimeSpan baseDelay)
    {
        if (baseDelay <= TimeSpan.Zero) return TimeSpan.Zero;
        var scaled = baseDelay.TotalMilliseconds * Math.Pow(DefaultBackoffFactor, attempt - 1);
        var capped = Math.Min(scaled, DefaultMaxDelay.TotalMilliseconds);
        var jitter = Random.Shared.Next(0, 250);
        return TimeSpan.FromMilliseconds(capped + jitter);
    }
}
