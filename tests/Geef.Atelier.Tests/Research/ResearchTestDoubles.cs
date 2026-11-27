using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Research;
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

    public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
    {
        Profiles.Enqueue((provider, model, maxTokens));
        return (client, model, maxTokens ?? 0);
    }

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

/// <summary>A clock that stands still at <paramref name="now"/>; elapsed time still runs on the system timer.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

/// <summary>
/// Orders in memory with only the members the research uses; the research methods behave like the real ones (conditional write,
/// count from an instant) unless a test replaces them.
/// </summary>
internal sealed class ResearchOrders : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, OrderEntity> _orders = new();

    public int Gets;
    public int Sets;
    public int Counts;

    /// <summary>Reservations of other orders counted in addition to the stored orders, by the daily count and the reservation.</summary>
    public int ExtraCount { get; set; }

    /// <summary>Thrown by the reservation when set.</summary>
    public Exception? CountFailure { get; set; }

    /// <summary>Thrown by the write when set.</summary>
    public Exception? SetFailure { get; set; }

    /// <summary>Runs before the write, for example to let another run store first.</summary>
    public Action<Guid>? BeforeSet { get; set; }

    public OrderEntity Put(OrderEntity order)
    {
        _orders[order.Id] = order;
        return order;
    }

    public OrderEntity? Find(Guid id) => _orders.TryGetValue(id, out var order) ? order : null;

    public Task<OrderEntity?> GetAsync(Guid orderId, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Gets);
        return Task.FromResult(Find(orderId));
    }

    public Task<bool> TrySetResearchAsync(Guid orderId, string researchJson, DateTimeOffset? at, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Sets);
        BeforeSet?.Invoke(orderId);
        if (SetFailure is { } failure)
            throw failure;
        while (_orders.TryGetValue(orderId, out var order))
        {
            if (order.ResearchJson is not null)
                return Task.FromResult(false);
            if (_orders.TryUpdate(orderId, order with { ResearchJson = researchJson, ResearchAt = order.ResearchAt ?? at }, order))
                return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    public Task<int> CountResearchSinceAsync(DateTimeOffset since, CancellationToken ct = default) =>
        Task.FromResult(ExtraCount + _orders.Values.Count(o => o.ResearchAt is { } at && at >= since));

    public Task<bool> TryReserveResearchAsync(Guid orderId, string? user, DateTimeOffset at, DateTimeOffset since, int dailyCap, int userCap, CancellationToken ct = default)
    {
        Interlocked.Increment(ref Counts);
        if (CountFailure is { } failure)
            throw failure;
        lock (_orders)
        {
            if (!_orders.TryGetValue(orderId, out var order) || order.ResearchJson is not null)
                return Task.FromResult(false);
            var others = _orders.Values.Where(o => o.Id != orderId && o.ResearchAt is { } r && r >= since && r < since.AddDays(1)).ToList();
            if (ExtraCount + others.Count >= dailyCap || others.Count(o => (o.CreatedByUser ?? string.Empty) == (user ?? string.Empty)) >= userCap)
                return Task.FromResult(false);
            _orders[orderId] = order with { ResearchAt = at };
            return Task.FromResult(true);
        }
    }

    public Task CreateWithFirstRunAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, RunEntity run, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CreateAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> AddRunAsync(Guid orderId, RunEntity run, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(RevisionStart Result, int? Version)> TryAddRevisionRunAsync(Guid orderId, Guid baseRunId, RunEntity run, int maxVersions, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<RunEntity>> ListRunsAsync(Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrderListRow>> ListAsync(string? ownerUsername, int limit, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<RunEventRow>> ListRunEventsAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> TrySetStateAsync(Guid orderId, OrderState expected, OrderState next, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(Guid OrderId, int? Version)?> GetLinkForRunAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task TouchAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> ListIntakeCandidatesAsync(DateTimeOffset staleBefore, int maxAttempts, int limit, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> TryClaimForIntakeAsync(Guid orderId, DateTimeOffset now, DateTimeOffset staleBefore, int maxAttempts, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> ListStuckIntakesAsync(DateTimeOffset staleBefore, DateTimeOffset unclaimedBefore, int maxAttempts, int limit, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> TrySaveQuestionsAsync(Guid orderId, string clarifyJson, DateTimeOffset at, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> TryStartWithRunAsync(OrderStart start, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> ListUnansweredAsync(DateTimeOffset askedBefore, int limit, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Search stand-in: answers per topic and records every topic searched.</summary>
internal sealed class FakeSearch(Func<string, TavilySearchOutcome>? answer = null) : ITavilySearchClient
{
    private readonly ConcurrentQueue<string> _queries = new();

    public IReadOnlyList<string> Queries => _queries.ToList();

    public TimeSpan Delay { get; set; }

    public async Task<TavilySearchOutcome> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        _queries.Enqueue(query);
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, ct);
        return (answer ?? (_ => new([], null, 1)))(query);
    }
}

/// <summary>Passage-picker stand-in: returns a fixed outcome and records what it was given.</summary>
internal sealed class FakeCondenser(Func<IReadOnlyList<string>, IReadOnlyList<ResearchPage>, CondenseOutcome> answer) : IDossierCondenser
{
    private readonly ConcurrentQueue<(IReadOnlyList<string> Topics, IReadOnlyList<ResearchPage> Pages)> _calls = new();

    public IReadOnlyList<(IReadOnlyList<string> Topics, IReadOnlyList<ResearchPage> Pages)> Calls => _calls.ToList();

    public Task<CondenseOutcome> CondenseAsync(IReadOnlyList<string> topics, IReadOnlyList<ResearchPage> pages, CancellationToken ct)
    {
        _calls.Enqueue((topics, pages));
        return Task.FromResult(answer(topics, pages));
    }
}
