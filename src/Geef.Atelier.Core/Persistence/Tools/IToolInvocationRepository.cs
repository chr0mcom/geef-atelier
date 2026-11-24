using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Core.Persistence.Tools;

/// <summary>Persistence contract for <see cref="ToolInvocation"/> audit records.</summary>
public interface IToolInvocationRepository
{
    /// <summary>
    /// Persists a new invocation record. The run-wide <see cref="ToolInvocation.Sequence"/> is assigned atomically
    /// by the database (next free number of the run, starting at 0); the value on <paramref name="invocation"/> is
    /// ignored, so concurrent or repeated callers never collide on the per-run unique index.
    /// </summary>
    Task AddAsync(ToolInvocation invocation, CancellationToken ct = default);

    /// <summary>
    /// Returns all invocation records for the given run, ordered by <see cref="ToolInvocation.Sequence"/>.
    /// </summary>
    Task<IReadOnlyList<ToolInvocation>> GetByRunIdAsync(Guid runId, CancellationToken ct = default);
}
