using System.Text.Json;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tools.FactCheckEval;

namespace Geef.Atelier.Tools.ResearchEval;

/// <summary>
/// The hard limits of one invocation at the transport, independent of the code under test: every Tavily request and every model call
/// takes one unit of its <see cref="CallBudget"/>; a search may only carry a topic the query gate sent for the current order; a model
/// call must switch the gateway failover off (otherwise the gateway could move it to Codex) and go to the Claude route. A refused
/// request is never sent; <see cref="Refusal"/> says why and the run stops.
/// </summary>
public sealed class ResearchTransport(CallBudget searches, CallBudget claude)
{
    private readonly object _gate = new();
    private HashSet<string> _allowed = new(StringComparer.Ordinal);
    private int _tavilyRequests;
    private int _claudeCalls;

    /// <summary>Budget of Tavily requests.</summary>
    public CallBudget Searches { get; } = searches;

    /// <summary>Budget of model calls.</summary>
    public CallBudget Claude { get; } = claude;

    /// <summary>Why a request was refused, or null.</summary>
    public string? Refusal { get; private set; }

    /// <summary>Raw JSON of the last model answer, or null when there was none.</summary>
    public string? LastRaw { get; private set; }

    /// <summary>Tavily requests sent since the last <see cref="BeginOrder"/>.</summary>
    public int TavilyRequests => Volatile.Read(ref _tavilyRequests);

    /// <summary>Model calls sent since the last <see cref="BeginOrder"/>.</summary>
    public int ClaudeCalls => Volatile.Read(ref _claudeCalls);

    /// <summary>Starts the counters of an order that may search exactly <paramref name="sentTopics"/>.</summary>
    public void BeginOrder(IEnumerable<string> sentTopics)
    {
        lock (_gate)
        {
            _allowed = new HashSet<string>(sentTopics, StringComparer.Ordinal);
            _tavilyRequests = 0;
            _claudeCalls = 0;
            LastRaw = null;
        }
    }

    /// <summary>Checks and counts one Tavily request with JSON body <paramref name="body"/>; false (and <see cref="Refusal"/> set) when it may not go out.</summary>
    public bool TakeSearch(string? body)
    {
        lock (_gate)
        {
            if (Refusal is not null)
                return false;
            var query = Query(body);
            if (query is null || !_allowed.Contains(query))
                return Refuse("a search request carried something other than a topic the query gate sent for this order");
            if (!Searches.TryTake())
                return Refuse("the search cap of this run is reached");
            _tavilyRequests++;
            return true;
        }
    }

    /// <summary>Checks and counts one model call; false (and <see cref="Refusal"/> set) when it may not go out.</summary>
    public bool TakeCall(LlmRequest request)
    {
        lock (_gate)
        {
            if (Refusal is not null)
                return false;
            if (!request.DisableFailover)
                return Refuse("a model call did not switch the gateway failover off, so it could have reached Codex");
            if (!Claude.TryTake())
                return Refuse("the model call cap of this run is reached");
            _claudeCalls++;
            return true;
        }
    }

    /// <summary>Keeps the raw JSON of a model answer.</summary>
    public void Answered(string? raw)
    {
        lock (_gate)
            LastRaw = raw;
    }

    private bool Refuse(string reason)
    {
        Refusal = reason;
        return false;
    }

    private static string? Query(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("query", out var query)
                && query.ValueKind == JsonValueKind.String
                ? query.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Lets a Tavily request out only when <see cref="ResearchTransport.TakeSearch"/> allows it; otherwise it fails without a network call.</summary>
public sealed class SearchGuardHandler(ResearchTransport transport) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        if (!transport.TakeSearch(body))
            throw new HttpRequestException($"Request refused by the measurement: {transport.Refusal}");
        return await base.SendAsync(request, cancellationToken);
    }
}

/// <summary>Hands the Tavily client a fresh client over one shared handler, so disposing it never closes the transport.</summary>
public sealed class SharedHandlerClientFactory(HttpMessageHandler handler, Uri baseAddress, TimeSpan timeout) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = baseAddress, Timeout = timeout };
}

/// <summary>A model client that lets a call out only when <see cref="ResearchTransport.TakeCall"/> allows it and keeps the raw answer.</summary>
public sealed class GuardedLlmClient(ILlmClient inner, ResearchTransport transport) : ILlmClient
{
    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken)
    {
        if (!transport.TakeCall(request))
            throw new InvalidOperationException($"Call refused by the measurement: {transport.Refusal}");
        var response = await inner.CompleteAsync(request, cancellationToken);
        transport.Answered(StructuredOutput.ExtractJson(response));
        return response;
    }
}

/// <summary>Resolves only the Claude subscription CLI and wraps its client in a <see cref="GuardedLlmClient"/>; Codex is never measured in VR.</summary>
public sealed class GuardedResolver(ILlmClientResolver inner, ResearchTransport transport) : ILlmClientResolver
{
    public (ILlmClient Client, string Model, int MaxTokens) ForActor(string actorName) =>
        throw new NotSupportedException("The measurement uses profiles only.");

    public (ILlmClient Client, string Model, int MaxTokens) ForProfile(string provider, string model, int? maxTokens)
    {
        if (provider != ModelMatrix.ClaudeCli)
            throw new InvalidOperationException($"Provider {provider} is not allowed in VR; only {ModelMatrix.ClaudeCli} (no Codex calls).");
        var (client, resolved, tokens) = inner.ForProfile(provider, model, maxTokens);
        return (new GuardedLlmClient(client, transport), resolved, tokens);
    }

    public bool SupportsAgenticTools(string providerName) => false;

    public bool SupportsStructuredOutputs(string providerName) => true;
}

/// <summary>Always assigns the given model; the tool has no live catalog and no database.</summary>
public sealed class FixedModelAssignment(ModelCandidate candidate) : IModelAssignmentService
{
    public Task<ModelAssignment> AssignAsync(ModelSlot slot, ModelDomain domain, ModelCandidate? executor = null, CancellationToken ct = default) =>
        Task.FromResult(new ModelAssignment(candidate, false, null));

    public Task<IReadOnlyList<ModelAssignment>> AssignReviewersAsync(IReadOnlyList<ReviewerAssignmentRequest> reviewers, ModelDomain crewDomain, ModelCandidate executor, CancellationToken ct = default) =>
        throw new NotSupportedException("The research measurement assigns only the passage picker.");

    public Task<CrewSnapshot> ApplyAsync(CrewSnapshot snapshot, ICollection<ModelAssignmentWarning> warnings, CancellationToken ct = default) =>
        throw new NotSupportedException("The research measurement assigns only the passage picker.");
}
