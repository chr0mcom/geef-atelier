using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Research;

/// <summary>Answers every request with a function of request and body and records both.</summary>
internal sealed class StubHttpHandler(Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    private readonly ConcurrentQueue<(HttpRequestMessage Request, string Body)> _requests = new();

    public IReadOnlyList<(HttpRequestMessage Request, string Body)> Requests => _requests.ToList();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        _requests.Enqueue((request, body));
        var response = await answer(request, body, cancellationToken);
        response.RequestMessage ??= request;
        return response;
    }

    public static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage Completion(string? content) => Json(new JsonObject
    {
        ["choices"] = new JsonArray(new JsonObject
        {
            ["message"] = new JsonObject { ["role"] = "assistant", ["content"] = content },
            ["finish_reason"] = "stop",
        }),
        ["usage"] = new JsonObject { ["prompt_tokens"] = 100, ["completion_tokens"] = 50, ["total_tokens"] = 150 },
    }.ToJsonString());
}

/// <summary>Assigns one fixed model to every slot and records the slots asked for.</summary>
internal sealed class FixedAssignments(string provider, string model) : IModelAssignmentService
{
    public ConcurrentQueue<(ModelSlot Slot, ModelDomain Domain)> Calls { get; } = new();

    public Task<ModelAssignment> AssignAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor = null, CancellationToken ct = default)
    {
        Calls.Enqueue((slot, domain));
        return Task.FromResult(new ModelAssignment(new ModelCandidate(provider, model), false, null));
    }

    public Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(IReadOnlyList<ReviewerAssignmentRequest> reviewers, ModelDomain crewDomain, ModelCandidate executor, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, ICollection<ModelAssignmentWarning> warnings, CancellationToken ct = default) =>
        throw new NotSupportedException();
}

/// <summary>Hands out one client for every profile.</summary>
internal sealed class SingleClientResolver(ILlmClient client, bool structured = true) : ILlmClientResolver
{
    public ConcurrentQueue<(string Provider, string Model, int? MaxTokens)> Profiles { get; } = new();

    public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) => throw new NotSupportedException();

    public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
    {
        Profiles.Enqueue((provider, model, maxTokens));
        return (client, model, maxTokens ?? 0);
    }

    public bool SupportsAgenticTools(string providerName) => false;

    public bool SupportsStructuredOutputs(string providerName) => structured;
}

/// <summary>Records every formatted log message with its level.</summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<(LogLevel Level, string Message)> _entries = new();

    public IReadOnlyList<(LogLevel Level, string Message)> Entries => _entries.ToList();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        _entries.Enqueue((logLevel, formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception)));
}
