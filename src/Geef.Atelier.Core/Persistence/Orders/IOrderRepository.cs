using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Core.Persistence.Orders;

/// <summary>An order with its current run and the number of linked sources, for lists.</summary>
public sealed record OrderListRow(OrderEntity Order, RunEntity? CurrentRun, int SourceCount);

/// <summary>A pipeline event of a run as needed for the progress display.</summary>
public sealed record RunEventRow(string EventType, int? Iteration, DateTimeOffset At);

/// <summary>Everything that changes when an order leaves the intake and its first run starts.</summary>
public sealed record OrderStart(
    Guid OrderId,
    OrderState Expected,
    RunEntity Run,
    string? ClarifyJson,
    string? AnswersJson,
    IntakeOutcome? Outcome,
    IReadOnlyList<(Guid SourceId, SourceRole Role)> RoleChanges,
    DateTimeOffset At);

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

    /// <summary>Lists orders in <see cref="OrderState.Analyzing"/> that are unclaimed or whose claim is older than <paramref name="staleBefore"/>, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListIntakeCandidatesAsync(DateTimeOffset staleBefore, int maxAttempts, int limit, CancellationToken ct = default);

    /// <summary>Atomically claims the order for the intake: sets ClaimedAt, IntakeStartedAt and increments IntakeAttempts when the order is Analyzing and unclaimed or stale and has fewer than <paramref name="maxAttempts"/> attempts.</summary>
    Task<bool> TryClaimForIntakeAsync(Guid orderId, DateTimeOffset now, DateTimeOffset staleBefore, int maxAttempts, CancellationToken ct = default);

    /// <summary>Orders that are Analyzing with exhausted attempts and a stale claim, or unclaimed for longer than <paramref name="unclaimedBefore"/>; they start without questions.</summary>
    Task<IReadOnlyList<Guid>> ListStuckIntakesAsync(DateTimeOffset staleBefore, DateTimeOffset unclaimedBefore, int maxAttempts, int limit, CancellationToken ct = default);

    /// <summary>Analyzing → NeedsInput with the record; sets QuestionsAskedAt, IntakeFinishedAt, IntakeOutcome = Asked. False when the state differed.</summary>
    Task<bool> TrySaveQuestionsAsync(Guid orderId, string clarifyJson, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>
    /// In one transaction: locks the order, checks <see cref="OrderStart.Expected"/>, sets state Ready, ClarifyJson (only when given),
    /// AnswersJson, IntakeOutcome, IntakeFinishedAt (when it was Analyzing), AnsweredAt (when it was NeedsInput), applies the role changes
    /// and inserts the run as version 1. False when the state differed or a version-1 run already exists (no exception).
    /// </summary>
    Task<bool> TryStartWithRunAsync(OrderStart start, CancellationToken ct = default);

    /// <summary>NeedsInput orders whose questions were asked before <paramref name="askedBefore"/>, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListUnansweredAsync(DateTimeOffset askedBefore, int limit, CancellationToken ct = default);

    /// <summary>
    /// Stores the research of the order only while the order has none; false when research was already stored. <c>ResearchAt</c>
    /// keeps the time of the reservation (<see cref="TryReserveResearchAsync"/>) and is set to <paramref name="at"/> only when it is
    /// empty and <paramref name="at"/> is given.
    /// </summary>
    Task<bool> TrySetResearchAsync(Guid orderId, string researchJson, DateTimeOffset? at, CancellationToken ct = default);

    /// <summary>
    /// Reserves a search for the order before it starts, in one step that no other reservation can interleave with: sets
    /// <c>ResearchAt</c> to <paramref name="at"/> while the order has no stored research, fewer than <paramref name="dailyCap"/> other
    /// orders and fewer than <paramref name="userCap"/> other orders of <paramref name="user"/> hold a reservation in the day that starts
    /// at <paramref name="since"/>. False when a limit is reached or the order is unknown or already has research. A reservation stays when
    /// the search is cancelled, so cancelled searches count; a second reservation of the same order does not count it twice.
    /// </summary>
    Task<bool> TryReserveResearchAsync(Guid orderId, string? user, DateTimeOffset at, DateTimeOffset since, int dailyCap, int userCap, CancellationToken ct = default);

    /// <summary>Number of orders with a reservation (<c>ResearchAt</c>) at or after <paramref name="since"/>.</summary>
    Task<int> CountResearchSinceAsync(DateTimeOffset since, CancellationToken ct = default);
}
