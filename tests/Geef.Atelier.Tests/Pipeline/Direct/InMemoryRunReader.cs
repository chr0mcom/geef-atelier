using System.Collections.Concurrent;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;

namespace Geef.Atelier.Tests.Pipeline.Direct;

/// <summary>
/// Runs of <see cref="DirectPipelineHarness"/> that a later run reads back (the earlier version of a change). Only
/// <see cref="GetByIdAsync"/> is served; the direct path needs nothing else.
/// </summary>
internal sealed class InMemoryRunReader : IRunRepository
{
    private readonly ConcurrentDictionary<Guid, RunEntity> _runs = new();

    /// <summary>Stores <paramref name="run"/> under its id, replacing an earlier entry.</summary>
    public void Put(RunEntity run) => _runs[run.Id] = run;

    /// <summary>When set, every read throws it (a failed read of the earlier version).</summary>
    public Exception? Failure { get; set; }

    public Task<RunEntity?> GetByIdAsync(Guid runId, CancellationToken cancellationToken = default) =>
        Failure is { } failure ? Task.FromException<RunEntity?>(failure) : Task.FromResult(_runs.TryGetValue(runId, out var run) ? run : null);

    public Task<IReadOnlyList<RunEntity>> ListAsync(int limit, RunStatus? statusFilter, string? username, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<bool> RequestCancellationAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<RunDetails?> GetDetailsAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task DeleteAsync(Guid runId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
