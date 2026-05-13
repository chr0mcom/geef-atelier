using System.Net;
using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

/// <summary>
/// Always throws a configurable exception from <see cref="CompleteAsync"/>.
/// Used to simulate LLM provider errors in orchestrator tests.
/// </summary>
internal sealed class ThrowingLlmClient(Exception exception) : ILlmClient
{
    /// <summary>Creates a client that throws <see cref="HttpRequestException"/> with the given HTTP status code.</summary>
    public static ThrowingLlmClient HttpError(HttpStatusCode statusCode) =>
        new(new HttpRequestException($"Response status code does not indicate success: {(int)statusCode}.", null, statusCode));

    /// <summary>Creates a client that throws <see cref="TaskCanceledException"/> (simulates a timeout).</summary>
    public static ThrowingLlmClient Timeout() =>
        new(new TaskCanceledException("The operation was canceled."));

    /// <summary>Creates a client that throws a generic <see cref="InvalidOperationException"/>.</summary>
    public static ThrowingLlmClient GenericError(string message = "Unexpected pipeline error.") =>
        new(new InvalidOperationException(message));

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct) =>
        Task.FromException<LlmResponse>(exception);
}
