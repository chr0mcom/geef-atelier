using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Core.Persistence.Orders;

/// <summary>An order with its current run and the number of linked sources, for lists.</summary>
public sealed record OrderListRow(OrderEntity Order, RunEntity? CurrentRun, int SourceCount);

/// <summary>A pipeline event of a run as needed for the progress display.</summary>
public sealed record RunEventRow(string EventType, int? Iteration, DateTimeOffset At);

/// <summary>
/// Persistence of orders and their link to runs. Every write that changes an order together with a run is one transaction; a
/// write that fails leaves nothing behind in the shared context, so that trying again does not add the first attempt twice.
/// </summary>
public interface IOrderRepository
{
    /// <summary>
    /// Inserts the order (state <see cref="OrderState.Ready"/>), its source links and its first run (version 1) in one
    /// transaction and clears <c>ExpiresAt</c> of the linked sources. Throws <see cref="OrderSourceMissingException"/> when
    /// a linked source was deleted in the meantime; nothing is stored then and a later call starts clean.
    /// </summary>
    Task CreateWithFirstRunAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, RunEntity run, CancellationToken ct = default);

    /// <summary>Inserts an order without a run (stage 2: state <see cref="OrderState.Analyzing"/>); throws <see cref="OrderSourceMissingException"/> like <see cref="CreateWithFirstRunAsync"/>.</summary>
    Task CreateAsync(OrderEntity order, IReadOnlyList<OrderSourceEntity> sources, CancellationToken ct = default);

    /// <summary>Adds a run as the next version (max version + 1, inside the transaction) and returns the version. Composition runs keep the version passed in <paramref name="run"/>.</summary>
    Task<int> AddRunAsync(Guid orderId, RunEntity run, CancellationToken ct = default);

    /// <summary>Returns the order, or <c>null</c> when it does not exist.</summary>
    Task<OrderEntity?> GetAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>Returns all runs of the order, of every kind.</summary>
    Task<IReadOnlyList<RunEntity>> ListRunsAsync(Guid orderId, CancellationToken ct = default);

    /// <summary>
    /// Lists orders newest activity first; <paramref name="ownerUsername"/> = null lists all users. The current run of a row is
    /// a slim copy for the list (status, outcome, kind, version, times, word count); its <see cref="RunEntity.FinalText"/>
    /// only tells whether a text exists, and briefing, configuration, crew snapshot and report are left empty.
    /// </summary>
    Task<IReadOnlyList<OrderListRow>> ListAsync(string? ownerUsername, int limit, CancellationToken ct = default);

    /// <summary>Returns the pipeline events of a run in the order they happened.</summary>
    Task<IReadOnlyList<RunEventRow>> ListRunEventsAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Atomically moves the order from <paramref name="expected"/> to <paramref name="next"/>; false when the state differed.</summary>
    Task<bool> TrySetStateAsync(Guid orderId, OrderState expected, OrderState next, CancellationToken ct = default);

    /// <summary>Returns the order and version a run belongs to, or <c>null</c> when the run has no order.</summary>
    Task<(Guid OrderId, int? Version)?> GetLinkForRunAsync(Guid runId, CancellationToken ct = default);

    /// <summary>Links a run created on the legacy path: inherits the order of the composition parent or the resumed parent, otherwise creates a <c>Careful</c>/<c>Ready</c> order.</summary>
    Task<Guid> LinkLegacyRunAsync(Guid runId, OrderEntry entry, CancellationToken ct = default);

    /// <summary>Sets <see cref="OrderEntity.UpdatedAt"/> to <paramref name="at"/>.</summary>
    Task TouchAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Deletes the order with its source links and runs; false when it did not exist.</summary>
    Task<bool> DeleteAsync(Guid orderId, CancellationToken ct = default);
}
