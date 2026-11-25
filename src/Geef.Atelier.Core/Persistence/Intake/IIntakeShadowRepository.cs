namespace Geef.Atelier.Core.Persistence.Intake;

/// <summary>Persistence of the shadow-mode intake records (<c>OrderIntakeShadows</c>).</summary>
public interface IIntakeShadowRepository
{
    /// <summary>Adds a pending shadow row for the order; does nothing when one exists.</summary>
    Task AddPendingAsync(Guid orderId, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Lists pending rows that are unclaimed or whose claim is older than <paramref name="staleBefore"/> and have fewer than <paramref name="maxAttempts"/> attempts, oldest first.</summary>
    Task<IReadOnlyList<Guid>> ListPendingAsync(DateTimeOffset staleBefore, int maxAttempts, int limit, CancellationToken ct = default);

    /// <summary>Atomically claims a pending row: sets ClaimedAt and increments Attempts; false when someone else holds it or the attempts are used up.</summary>
    Task<bool> TryClaimAsync(Guid orderId, DateTimeOffset now, DateTimeOffset staleBefore, int maxAttempts, CancellationToken ct = default);

    /// <summary>Stores the result of the shadow intake and sets CompletedAt.</summary>
    Task CompleteAsync(Guid orderId, string status, int? durationMs, int? questionCount, string? recordJson, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>Number of shadow rows completed since <paramref name="since"/> (daily cap).</summary>
    Task<int> CountCompletedSinceAsync(DateTimeOffset since, CancellationToken ct = default);
}
