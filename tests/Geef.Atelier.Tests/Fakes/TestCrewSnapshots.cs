using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// Serialized crew snapshots for tests that create runs directly. The orchestrator no longer falls back to a
/// default crew, so every run it should pick up needs a stored snapshot.
/// </summary>
internal static class TestCrewSnapshots
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>A snapshot of the test loop (<see cref="TestLoop"/>); an orchestrator with <see cref="TestLoopAssembler"/> runs it.</summary>
    public static CrewSnapshot Runnable { get; } = TestLoop.Snapshot();

    /// <summary><see cref="Runnable"/> as stored in <c>Runs.CrewSnapshot</c>.</summary>
    public static string RunnableJson { get; } = Serialize(Runnable);

    /// <summary>Serializes <paramref name="snapshot"/> the way runs are stored.</summary>
    public static string Serialize(CrewSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
}
