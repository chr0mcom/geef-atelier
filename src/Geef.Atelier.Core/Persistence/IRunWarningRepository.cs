using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Core.Persistence;

/// <summary>Read access to the warnings recorded for a run.</summary>
public interface IRunWarningRepository
{
    /// <summary>Returns the warnings of <paramref name="runId"/> in the order they were recorded.</summary>
    Task<IReadOnlyList<RunWarningEntity>> ListByRunAsync(Guid runId, CancellationToken ct = default);
}
