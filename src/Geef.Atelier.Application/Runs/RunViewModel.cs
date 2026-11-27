using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Application.Runs;

/// <summary>A run with what its technical page shows: the details, the setup it ran with and its warnings.</summary>
/// <param name="Details">The run entity together with its iterations and findings.</param>
/// <param name="Snapshot">The setup the run was built from, read tolerantly; null when none is stored or it cannot be read.</param>
/// <param name="Warnings">Problems the run worked around, in the order they were recorded; empty when none.</param>
public sealed record RunViewModel(
    RunDetails Details,
    CrewSnapshot? Snapshot,
    IReadOnlyList<Core.Domain.Warnings.RunWarningEntity> Warnings);
