using System.Collections.Concurrent;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Persistence.Runs;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>Keeps the fact-check mode per run in memory; a repeated call overwrites the mode.</summary>
internal sealed class RecordingRunFactCheckModeStore : IRunFactCheckModeStore
{
    public ConcurrentDictionary<Guid, FactCheckMode> Modes { get; } = new();

    public Task SetAsync(Guid runId, FactCheckMode mode, CancellationToken ct = default)
    {
        Modes[runId] = mode;
        return Task.CompletedTask;
    }
}
