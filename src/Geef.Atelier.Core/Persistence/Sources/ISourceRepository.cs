using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Core.Persistence.Sources;

/// <summary>A source together with its link to an order.</summary>
public sealed record OrderSourceItem(OrderSourceEntity Link, SourceEntity Source);

/// <summary>Persistence of uploaded sources, their order links and their use in runs. Never filters by user; callers check ownership.</summary>
public interface ISourceRepository
{
    /// <summary>Inserts a source.</summary>
    Task AddAsync(SourceEntity source, CancellationToken ct = default);

    /// <summary>Returns the source, with its extracted text only when <paramref name="includeText"/> is set.</summary>
    Task<SourceEntity?> GetAsync(Guid sourceId, bool includeText, CancellationToken ct = default);

    /// <summary>Returns the source of <paramref name="ownerUsername"/> with the given SHA-256, if any.</summary>
    Task<SourceEntity?> FindByHashAsync(string ownerUsername, string sha256, CancellationToken ct = default);

    /// <summary>Returns the existing sources among <paramref name="sourceIds"/>.</summary>
    Task<IReadOnlyList<SourceEntity>> GetManyAsync(IReadOnlyCollection<Guid> sourceIds, bool includeText, CancellationToken ct = default);

    /// <summary>Returns the sources of an order ordered by position.</summary>
    Task<IReadOnlyList<OrderSourceItem>> ListForOrderAsync(Guid orderId, bool includeText, CancellationToken ct = default);

    /// <summary>True when the source is linked to at least one order.</summary>
    Task<bool> IsAttachedAsync(Guid sourceId, CancellationToken ct = default);

    /// <summary>Deletes the source when no order uses it; false when it is linked or does not exist.</summary>
    Task<bool> DeleteIfUnattachedAsync(Guid sourceId, CancellationToken ct = default);

    /// <summary>Returns at most <paramref name="limit"/> unlinked sources whose <c>ExpiresAt</c> lies before <paramref name="now"/>.</summary>
    Task<IReadOnlyList<SourceEntity>> ListExpiredUnattachedAsync(DateTimeOffset now, int limit, CancellationToken ct = default);

    /// <summary>Stores which sources went into the prompts of a run.</summary>
    Task AddRunUsagesAsync(IReadOnlyList<RunSourceUsageEntity> usages, CancellationToken ct = default);

    /// <summary>Returns the source usages of a run ordered by position.</summary>
    Task<IReadOnlyList<RunSourceUsageEntity>> ListRunUsagesAsync(Guid runId, CancellationToken ct = default);
}
