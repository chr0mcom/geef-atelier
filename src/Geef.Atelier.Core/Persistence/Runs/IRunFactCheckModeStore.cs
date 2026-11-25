using Geef.Atelier.Core.Domain.FactCheck;

namespace Geef.Atelier.Core.Persistence.Runs;

/// <summary>Stores the fact-check mode of a run.</summary>
public interface IRunFactCheckModeStore
{
    /// <summary>Stores the mode the run is checked with (Runs.FactCheckMode). Never decides by difficulty, length or count.</summary>
    Task SetAsync(Guid runId, FactCheckMode mode, CancellationToken ct = default);
}
