using System.Collections.Concurrent;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence.Intake;
using Geef.Atelier.Core.Persistence.Orders;

namespace Geef.Atelier.Tests.Intake;

/// <summary>A time provider whose clock only moves when the test moves it; timers and timestamps come from the system.</summary>
internal sealed class ManualTime(DateTimeOffset start) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = start;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Orders in memory with the intake members of <see cref="IOrderRepository"/>; every call is recorded.</summary>
internal sealed class IntakeOrders : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, OrderEntity> _orders = new();

    public List<Guid> Candidates { get; } = [];
    public List<Guid> Stuck { get; } = [];
    public List<Guid> Unanswered { get; } = [];
    public HashSet<Guid> ClaimRefused { get; } = [];
    public HashSet<Guid> ClaimThrows { get; } = [];
    public bool SaveQuestionsResult { get; set; } = true;
    public ConcurrentQueue<(Guid OrderId, DateTimeOffset Now, DateTimeOffset StaleBefore, int MaxAttempts)> Claims { get; } = new();
    public ConcurrentQueue<(DateTimeOffset StaleBefore, int MaxAttempts, int Limit)> CandidateQueries { get; } = new();
    public ConcurrentQueue<(DateTimeOffset StaleBefore, DateTimeOffset UnclaimedBefore, int MaxAttempts)> StuckQueries { get; } = new();
    public ConcurrentQueue<DateTimeOffset> UnansweredQueries { get; } = new();
    public ConcurrentQueue<(Guid OrderId, string Json, DateTimeOffset At)> SavedQuestions { get; } = new();
    public int Writes => SavedQuestions.Count;

    public OrderEntity Add(string request, OrderState state = OrderState.Analyzing,
        string? user = "intake-user", int attempts = 0, string? clarifyJson = null)
    {
        var now = DateTimeOffset.UtcNow;
        var order = new OrderEntity
        {
            Id = Guid.NewGuid(), CreatedByUser = user, RequestText = request, State = state, Entry = OrderEntry.Web,
            IntakeAttempts = attempts, ClarifyJson = clarifyJson, CreatedAt = now, UpdatedAt = now,
        };
        _orders[order.Id] = order;
        return order;
    }

    public void Set(OrderEntity order) => _orders[order.Id] = order;

    public Task<OrderEntity?> GetAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult(_orders.TryGetValue(orderId, out var order) ? order : null);

    public Task<IReadOnlyList<Guid>> ListIntakeCandidatesAsync(DateTimeOffset staleBefore, int maxAttempts, int limit, CancellationToken ct = default)
    {
        CandidateQueries.Enqueue((staleBefore, maxAttempts, limit));
        lock (Candidates)
            return Task.FromResult<IReadOnlyList<Guid>>(Candidates.Take(limit).ToList());
    }

    public Task<bool> TryClaimForIntakeAsync(Guid orderId, DateTimeOffset now, DateTimeOffset staleBefore, int maxAttempts, CancellationToken ct = default)
    {
        Claims.Enqueue((orderId, now, staleBefore, maxAttempts));
        if (ClaimThrows.Contains(orderId))
            throw new InvalidOperationException("The connection was closed.");
        lock (Candidates)
        {
            if (ClaimRefused.Contains(orderId) || !Candidates.Remove(orderId))
                return Task.FromResult(false);
        }
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<Guid>> ListStuckIntakesAsync(DateTimeOffset staleBefore, DateTimeOffset unclaimedBefore, int maxAttempts, int limit, CancellationToken ct = default)
    {
        StuckQueries.Enqueue((staleBefore, unclaimedBefore, maxAttempts));
        return Task.FromResult<IReadOnlyList<Guid>>(Stuck.Take(limit).ToList());
    }

    public Task<bool> TrySaveQuestionsAsync(Guid orderId, string clarifyJson, DateTimeOffset at, CancellationToken ct = default)
    {
        SavedQuestions.Enqueue((orderId, clarifyJson, at));
        return Task.FromResult(SaveQuestionsResult);
    }

    public Task<IReadOnlyList<Guid>> ListUnansweredAsync(DateTimeOffset askedBefore, int limit, CancellationToken ct = default)
    {
        UnansweredQueries.Enqueue(askedBefore);
        return Task.FromResult<IReadOnlyList<Guid>>(Unanswered.Take(limit).ToList());
    }

    public Task<bool> TryStartWithRunAsync(OrderStart start, CancellationToken ct = default) => throw new NotSupportedException("The intake starts runs through IOrderRunStarter.");
    public Task<bool> TrySetStateAsync(Guid orderId, OrderState expected, OrderState next, CancellationToken ct = default) => throw new NotSupportedException("The intake never sets a state directly.");
    public Task CreateWithFirstRunAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, RunEntity run, CancellationToken ct = default) => throw new NotSupportedException();
    public Task CreateAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> AddRunAsync(Guid orderId, RunEntity run, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(RevisionStart Result, int? Version)> TryAddRevisionRunAsync(Guid orderId, Guid baseRunId, RunEntity run, int maxVersions, CancellationToken ct = default) => throw new NotSupportedException("The intake never changes a text.");
    public Task<IReadOnlyList<RunEntity>> ListRunsAsync(Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrderListRow>> ListAsync(string? ownerUsername, int limit, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<RunEventRow>> ListRunEventsAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<(Guid OrderId, int? Version)?> GetLinkForRunAsync(Guid runId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task TouchAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(Guid orderId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<bool> TrySetResearchAsync(Guid orderId, string researchJson, DateTimeOffset? at, CancellationToken ct = default) => throw new NotSupportedException("The intake never does research.");
    public Task<int> CountResearchSinceAsync(DateTimeOffset since, CancellationToken ct = default) => throw new NotSupportedException("The intake never does research.");
    public Task<bool> TryReserveResearchAsync(Guid orderId, string? user, DateTimeOffset at, DateTimeOffset since, int dailyCap, int userCap, CancellationToken ct = default) => throw new NotSupportedException("The intake never does research.");
}

/// <summary>Shadow rows in memory.</summary>
internal sealed class IntakeShadows : IIntakeShadowRepository
{
    public List<Guid> Pending { get; } = [];
    public int CompletedToday { get; set; }
    public ConcurrentQueue<(Guid OrderId, string Status, int? DurationMs, int? QuestionCount, string? RecordJson)> Completed { get; } = new();
    public ConcurrentQueue<DateTimeOffset> CountQueries { get; } = new();
    public int Lists { get; private set; }

    public Task AddPendingAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> ListPendingAsync(DateTimeOffset staleBefore, int maxAttempts, int limit, CancellationToken ct = default)
    {
        Lists++;
        lock (Pending)
            return Task.FromResult<IReadOnlyList<Guid>>(Pending.Take(limit).ToList());
    }

    public Task<bool> TryClaimAsync(Guid orderId, DateTimeOffset now, DateTimeOffset staleBefore, int maxAttempts, CancellationToken ct = default)
    {
        lock (Pending)
            return Task.FromResult(Pending.Remove(orderId));
    }

    public Task CompleteAsync(Guid orderId, string status, int? durationMs, int? questionCount, string? recordJson, DateTimeOffset at, CancellationToken ct = default)
    {
        Completed.Enqueue((orderId, status, durationMs, questionCount, recordJson));
        return Task.CompletedTask;
    }

    public Task<int> CountCompletedSinceAsync(DateTimeOffset since, CancellationToken ct = default)
    {
        CountQueries.Enqueue(since);
        return Task.FromResult(CompletedToday);
    }
}

/// <summary>Returns queued clarifier results (default: the configured one) and records every request.</summary>
internal sealed class ScriptedClarifier : IClarifier
{
    private readonly ConcurrentQueue<ClarifierCallResult> _results = new();

    public ConcurrentQueue<ClarifierRequest> Requests { get; } = new();
    public ClarifierCallResult Default { get; set; } = new(IntakeRecordStatus.Failed, null, null, 0, "not scripted");
    public TimeSpan Delay { get; set; }
    public Exception? Throw { get; set; }

    public void Enqueue(ClarifierOutput output) => _results.Enqueue(new ClarifierCallResult(IntakeRecordStatus.Ok, output, "claude-sonnet-latest", 1234, null));
    public void Enqueue(ClarifierCallResult result) => _results.Enqueue(result);

    public async Task<ClarifierCallResult> ClarifyAsync(ClarifierRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        if (Delay > TimeSpan.Zero)
            await Task.Delay(Delay, ct);
        if (Throw is not null)
            throw Throw;
        return _results.TryDequeue(out var result) ? result : Default;
    }
}

/// <summary>Records every start; answers with a new run id unless <see cref="Refuse"/> is set.</summary>
internal sealed class RecordingRunStarter : IOrderRunStarter
{
    public ConcurrentQueue<RunStartRequest> Requests { get; } = new();
    public bool Refuse { get; set; }
    public HashSet<Guid> ThrowFor { get; } = [];

    public Task<Guid?> TryStartAsync(RunStartRequest request, CancellationToken ct)
    {
        Requests.Enqueue(request);
        if (ThrowFor.Contains(request.Order.Id))
            throw new InvalidOperationException("The crew of this order cannot be resolved.");
        return Task.FromResult<Guid?>(Refuse ? null : Guid.NewGuid());
    }
}

/// <summary>Records notified orders.</summary>
internal sealed class RecordingOrderNotifier : IOrderNotifier
{
    public ConcurrentQueue<Guid> Notified { get; } = new();

    public Task NotifyOrderUpdatedAsync(Guid orderId, CancellationToken ct = default)
    {
        Notified.Enqueue(orderId);
        return Task.CompletedTask;
    }
}

/// <summary>Memory resolution with fixed facts and answers.</summary>
internal sealed class FixedMemory(MemoryFacts? facts = null, IReadOnlyDictionary<GapTopic, MemoryAnswer>? known = null) : IMemoryResolution
{
    public List<string> Loaded { get; } = [];
    public Exception? Throw { get; set; }

    public Task<MemoryFacts> LoadAsync(string username, CancellationToken ct = default)
    {
        Loaded.Add(username);
        if (Throw is not null)
            throw Throw;
        return Task.FromResult(facts ?? new MemoryFacts(null));
    }

    public IReadOnlyDictionary<GapTopic, MemoryAnswer> Resolve(MemoryFacts facts, string? textType) =>
        known ?? new Dictionary<GapTopic, MemoryAnswer>();
}
