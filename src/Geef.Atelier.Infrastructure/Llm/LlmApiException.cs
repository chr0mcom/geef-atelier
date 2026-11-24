using System.Net;

namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>
/// An LLM provider answered with an error status. It is an <see cref="HttpRequestException"/> so that retry
/// classification and user messages treat it exactly like before; <see cref="ErrorCode"/> carries the
/// machine-readable <c>error.code</c> of the answer when there is one (the cli-proxy sends for example
/// <see cref="EmptyResultCode"/> and <see cref="BackendUnavailableCode"/>).
/// </summary>
internal sealed class LlmApiException(string message, HttpStatusCode statusCode, string? errorCode)
    : HttpRequestException(message, null, statusCode)
{
    /// <summary>cli-proxy: the CLI left the document empty and unchanged (HTTP 502).</summary>
    public const string EmptyResultCode = "empty_result";

    /// <summary>cli-proxy: the requested CLI is unavailable and failover was switched off (HTTP 503).</summary>
    public const string BackendUnavailableCode = "backend_unavailable";

    /// <summary>The <c>error.code</c> of the provider's answer, or <c>null</c>.</summary>
    public string? ErrorCode { get; } = errorCode;
}
