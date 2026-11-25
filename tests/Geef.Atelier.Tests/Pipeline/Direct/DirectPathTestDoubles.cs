using System.Collections.Concurrent;
using Geef.Atelier.Application.Research;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Sources;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>Orders in memory; only the members the direct path uses are implemented.</summary>
internal sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, OrderEntity> _orders = new();
    private readonly ConcurrentDictionary<Guid, Guid> _runLinks = new();

    public ConcurrentQueue<(Guid OrderId, DateTimeOffset At)> Touches { get; } = new();

    public OrderEntity Add(string requestText, Guid? runId = null)
    {
        var now = DateTimeOffset.UtcNow;
        var order = new OrderEntity
        {
            Id = Guid.NewGuid(), CreatedByUser = "e2e-user", RequestText = requestText, Mode = OrderMode.Direct,
            State = OrderState.Ready, Entry = OrderEntry.Web, CreatedAt = now, UpdatedAt = now,
        };
        _orders[order.Id] = order;
        if (runId is { } id)
            _runLinks[id] = order.Id;
        return order;
    }

    public Task<OrderEntity?> GetAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_orders.TryGetValue(orderId, out var order) ? order : null);

    public Task<(Guid OrderId, int? Version)?> GetLinkForRunAsync(Guid runId, CancellationToken ct = default) =>
        Task.FromResult<(Guid OrderId, int? Version)?>(_runLinks.TryGetValue(runId, out var orderId) ? (orderId, 1) : null);

    public Task TouchAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default)
    {
        Touches.Enqueue((orderId, at));
        return Task.CompletedTask;
    }

    public Task CreateWithFirstRunAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, RunEntity run, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CreateAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> AddRunAsync(Guid orderId, RunEntity run, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<RunEntity>> ListRunsAsync(Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrderListRow>> ListAsync(string? ownerUsername, int limit, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<RunEventRow>> ListRunEventsAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> TrySetStateAsync(Guid orderId, OrderState expected, OrderState next, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<Guid> LinkLegacyRunAsync(Guid runId, OrderEntry entry, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> ListIntakeCandidatesAsync(DateTimeOffset staleBefore, int maxAttempts, int limit, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> TryClaimForIntakeAsync(Guid orderId, DateTimeOffset now, DateTimeOffset staleBefore, int maxAttempts, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<Guid>> ListStuckIntakesAsync(DateTimeOffset staleBefore, DateTimeOffset unclaimedBefore, int maxAttempts, int limit, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> TrySaveQuestionsAsync(Guid orderId, string clarifyJson, DateTimeOffset at, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<bool> TryStartWithRunAsync(OrderStart start, CancellationToken ct = default) => throw new NotImplementedException();
    public Task<IReadOnlyList<Guid>> ListUnansweredAsync(DateTimeOffset askedBefore, int limit, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<bool> TrySetResearchAsync(Guid orderId, string researchJson, DateTimeOffset? at, CancellationToken ct = default)
    {
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
        Task.FromResult(_orders.Values.Count(o => o.ResearchAt is { } at && at >= since));

    public Task<bool> TryReserveResearchAsync(Guid orderId, string? user, DateTimeOffset at, DateTimeOffset since, int dailyCap, int userCap, CancellationToken ct = default)
    {
        lock (_orders)
        {
            if (!_orders.TryGetValue(orderId, out var order) || order.ResearchJson is not null)
                return Task.FromResult(false);
            var others = _orders.Values.Where(o => o.Id != orderId && o.ResearchAt is { } r && r >= since && r < since.AddDays(1)).ToList();
            if (others.Count >= dailyCap || others.Count(o => (o.CreatedByUser ?? string.Empty) == (user ?? string.Empty)) >= userCap)
                return Task.FromResult(false);
            _orders[orderId] = order with { ResearchAt = at };
            return Task.FromResult(true);
        }
    }
}

/// <summary>Sources, order links and run usages in memory; only the members the direct path uses are implemented.</summary>
internal sealed class InMemorySourceRepository : ISourceRepository
{
    private readonly ConcurrentDictionary<Guid, List<OrderSourceItem>> _links = new();
    private readonly ConcurrentQueue<RunSourceUsageEntity> _usages = new();

    public IReadOnlyList<RunSourceUsageEntity> Usages => _usages.ToList();

    public SourceEntity Attach(Guid orderId, string fileName, SourceKind kind, string text, int? pageCount = null,
        SourceRole role = SourceRole.Facts, string? pageSelection = null, IReadOnlyList<SourceWarning>? warnings = null, int? position = null)
    {
        var list = _links.GetOrAdd(orderId, _ => []);
        var source = new SourceEntity
        {
            Id = Guid.NewGuid(), OwnerUsername = "e2e-user", FileName = fileName, ContentType = "text/plain", Kind = kind,
            SizeBytes = text.Length, Sha256 = new string('0', 64), ExtractedText = text, PageCount = pageCount,
            CharCount = text.CharCountWithoutPageMarkers(),
            WordCount = 0, Quality = SourceQuality.Good, Warnings = warnings ?? [], Origin = SourceOrigin.WebUpload,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        lock (list)
        {
            list.Add(new OrderSourceItem(new OrderSourceEntity
            {
                OrderId = orderId, SourceId = source.Id, Role = role, Position = position ?? list.Count, PageSelection = pageSelection,
                AddedAt = DateTimeOffset.UtcNow,
            }, source));
        }
        return source;
    }

    public Task<IReadOnlyList<OrderSourceItem>> ListForOrderAsync(Guid orderId, bool includeText, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrderSourceItem>>(_links.TryGetValue(orderId, out var list) ? list.ToList() : []);

    public Task AddRunUsagesAsync(IReadOnlyList<RunSourceUsageEntity> usages, CancellationToken ct = default)
    {
        foreach (var usage in usages)
            _usages.Enqueue(usage);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RunSourceUsageEntity>> ListRunUsagesAsync(Guid runId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<RunSourceUsageEntity>>(_usages.Where(u => u.RunId == runId).OrderBy(u => u.Position).ToList());

    public Task AddAsync(SourceEntity source, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SourceEntity?> GetAsync(Guid sourceId, bool includeText, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<SourceEntity?> FindByHashAsync(string ownerUsername, string sha256, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<SourceEntity>> GetManyAsync(IReadOnlyCollection<Guid> sourceIds, bool includeText, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> IsAttachedAsync(Guid sourceId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteIfUnattachedAsync(Guid sourceId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<SourceEntity>> ListExpiredUnattachedAsync(DateTimeOffset now, int limit, CancellationToken ct = default) => throw new NotSupportedException();
}

/// <summary>Keeps every stored report.</summary>
internal sealed class RecordingRunReportWriter : IRunReportWriter
{
    public ConcurrentDictionary<Guid, string> Reports { get; } = new();

    public Task WriteAsync(Guid runId, string reportJson, CancellationToken ct = default)
    {
        Reports[runId] = reportJson;
        return Task.CompletedTask;
    }
}

/// <summary>Keeps every grounding consultation.</summary>
internal sealed class RecordingConsultationRepository : IGroundingConsultationRepository
{
    public ConcurrentQueue<GroundingConsultation> Created { get; } = new();

    public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
    {
        Created.Enqueue(consultation);
        return Task.FromResult(consultation);
    }

    public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<GroundingConsultation>>(Created.Where(c => c.RunId == runId).ToList());

    public Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>One call of <see cref="IOrderResearchService.EnsureAsync"/> as the grounding step made it.</summary>
internal sealed record ResearchCall(Guid OrderId, string RequestText, int? OrderVersion, bool Revises, bool HasFiles, string? ProfileText);

/// <summary>
/// Stand-in for the research service of stage 4: records every call and answers with <see cref="Answer"/> (default: no research). With
/// <see cref="StoreFirst"/> it behaves like the real service towards the step: the first answer of an order is kept and every later call of
/// that order gets the kept one, counted as <see cref="NewResearch"/> only once.
/// </summary>
internal sealed class ScriptedOrderResearchService : IOrderResearchService
{
    private readonly ConcurrentDictionary<Guid, OrderResearch> _stored = new();
    private int _newResearch;

    public ConcurrentQueue<ResearchCall> Calls { get; } = new();

    public Func<ResearchCall, CancellationToken, Task<OrderResearch?>> Answer { get; set; } = (_, _) => Task.FromResult<OrderResearch?>(null);

    public bool StoreFirst { get; set; }

    public int NewResearch => _newResearch;

    public async Task<OrderResearch?> EnsureAsync(OrderEntity order, int? orderVersion, bool revises, bool hasFiles, string? profileText, CancellationToken ct)
    {
        var call = new ResearchCall(order.Id, order.RequestText, orderVersion, revises, hasFiles, profileText);
        Calls.Enqueue(call);
        if (StoreFirst && _stored.TryGetValue(order.Id, out var stored))
            return stored;

        var research = await Answer(call, ct);
        if (research is not null)
        {
            Interlocked.Increment(ref _newResearch);
            if (StoreFirst)
                _stored.TryAdd(order.Id, research);
        }
        return research;
    }

    /// <summary>Answers every call with <paramref name="research"/>.</summary>
    public ScriptedOrderResearchService Returning(OrderResearch? research)
    {
        Answer = (_, _) => Task.FromResult(research);
        return this;
    }
}

/// <summary>Research results for the tests of stage 4; pages and passages are synthetic.</summary>
internal static class ResearchSamples
{
    public static readonly DateTimeOffset At = new(2026, 11, 28, 9, 30, 0, TimeSpan.Zero);

    public static ResearchEntry Entry(int n, string quote, string url = "https://wages.example/minimum", string title = "Minimum wage 2027") =>
        new($"W1.{n}", 0, quote, url, title, At, QuoteMatchKind.Exact, new string('a', 64), 0, quote.Length);

    public static OrderResearch Found(params ResearchEntry[] entries) => With(ResearchStatus.Found, entries);

    public static OrderResearch With(ResearchStatus status, IReadOnlyList<ResearchEntry>? entries = null, string? errorCode = null) =>
        new(OrderResearch.CurrentSchemaVersion, status, ["Mindestlohn 2027 Deutschland"], 0, 0, 1, 0, entries?.Count ?? 0, entries?.Count ?? 0,
            new Dictionary<ResearchDropReason, int>(), entries ?? [], "claude-sonnet", 1200, errorCode, At);
}
