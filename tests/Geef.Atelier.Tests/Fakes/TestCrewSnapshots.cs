using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// Serialized crew snapshots for tests that create runs directly. The orchestrator no longer falls back to a
/// default crew, so every run it should pick up needs a stored snapshot.
/// </summary>
internal static class TestCrewSnapshots
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The classic crew without grounding, as the orchestrator's former fallback built it.</summary>
    public static CrewSnapshot Klassik { get; } = new(
        SchemaVersion: CrewSnapshot.CurrentSchemaVersion,
        TemplateName: SystemCrew.KlassikTemplateName,
        Executor: SystemCrew.DefaultExecutorProfile,
        Reviewers: [SystemCrew.BriefingFidelityProfile, SystemCrew.ClarityProfile],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        Advisors: Array.Empty<AdvisorProfile>());

    /// <summary><see cref="Klassik"/> as stored in <c>Runs.CrewSnapshot</c>.</summary>
    public static string KlassikJson { get; } = Serialize(Klassik);

    /// <summary>Serializes <paramref name="snapshot"/> the way <c>RunService</c> stores it.</summary>
    public static string Serialize(CrewSnapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);
}
