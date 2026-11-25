namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>
/// The machine-readable <c>error.code</c> of a provider's error answer. The client keeps throwing a plain
/// <see cref="HttpRequestException"/> (retry classification and user messages stay as they are) and stores the code
/// in <see cref="Exception.Data"/>; the cli-proxy sends for example <see cref="EmptyResultCode"/> and
/// <see cref="BackendUnavailableCode"/>.
/// </summary>
internal static class LlmApiError
{
    /// <summary>Key of the error code in <see cref="Exception.Data"/>.</summary>
    public const string DataKey = "geef:llm-error-code";

    /// <summary>cli-proxy: the CLI left the document empty and unchanged (HTTP 502).</summary>
    public const string EmptyResultCode = "empty_result";

    /// <summary>cli-proxy: the requested CLI is unavailable and failover was switched off (HTTP 503).</summary>
    public const string BackendUnavailableCode = "backend_unavailable";

    /// <summary>
    /// cli-proxy: the CLI process ran into the time limit the caller sent in <c>x-cli-proxy-timeout-seconds</c> and was ended
    /// together with its child processes (HTTP 504). Neither a breaker failure nor a failover.
    /// </summary>
    public const string CliTimeoutCode = "cli_timeout";

    /// <summary>Attaches <paramref name="code"/> to <paramref name="exception"/> when there is one.</summary>
    public static HttpRequestException WithErrorCode(this HttpRequestException exception, string? code)
    {
        if (code is not null)
            exception.Data[DataKey] = code;
        return exception;
    }

    /// <summary>The error code attached by the client, or <c>null</c>.</summary>
    public static string? ErrorCode(this HttpRequestException exception) => exception.Data[DataKey] as string;
}
