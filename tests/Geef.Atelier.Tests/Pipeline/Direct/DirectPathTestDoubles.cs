using System.Collections.Concurrent;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Orders;
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
