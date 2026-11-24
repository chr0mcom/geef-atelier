using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Frozen, fully-dereferenced snapshot of the crew configuration used for a single run.
/// Persisted as JSONB on <c>RunEntity.CrewSnapshot</c> so that runs remain reproducible even
/// after referenced profiles are renamed or deleted.
/// </summary>
/// <param name="SchemaVersion">
/// Format version. Increment when the snapshot schema changes; <see cref="DeserializeForRun"/> throws on
/// unknown versions to surface format drift early.
/// </param>
/// <param name="TemplateName">
/// Name of the template the snapshot was built from, or null when the run was submitted with
/// an inline <see cref="CrewSpec"/>.
/// </param>
/// <param name="Executor">Embedded executor profile (full content, not just a reference).</param>
/// <param name="Reviewers">Embedded reviewer profiles in the order required by the evaluation strategy.</param>
/// <param name="EvaluationStrategy">Strategy used for reviewer orchestration.</param>
/// <param name="ConvergenceOverride">Convergence-policy overrides applied for this run, if any.</param>
/// <param name="Advisors">Embedded advisor profiles. Empty when no advisors are configured.</param>
/// <param name="GroundingProviders">
/// Fully-dereferenced grounding-provider profiles. Empty for templates without web-research.
/// Grounding runs once at the start of a run (not per iteration).
/// </param>
/// <param name="Finalizers">
/// Fully-dereferenced finalizer profiles in execution order. Null or empty for templates without
/// post-processing steps. Trailing-optional so snapshots created before this field was introduced
/// remain deserializable.
/// </param>
/// <param name="RunFinalizersOnMaxAttempts">
/// When <c>true</c>, finalizers also execute if the run exhausts max iterations without converging.
/// Trailing-optional; defaults to <c>false</c> for backward compatibility.
/// </param>
/// <param name="ToolBindings">
/// Fully-dereferenced tool definitions keyed by actor profile name (e.g. <c>"briefing-fidelity"</c>).
/// Populated at snapshot-build time for every actor that declares <c>ToolNames</c>, so that tool
/// semantics remain reproducible even after the catalogue is updated.
/// <c>null</c> when no actor in this snapshot uses tools (schema v1 snapshots also read as null).
/// Trailing-optional for backward compatibility with v1 snapshots.
/// </param>
/// <param name="PromptCompositions">
/// Per-actor record of how each generic role prompt was composed with its bound specialization packs
/// (role prompt + composed effective prompt + pack provenance). Populated at snapshot-build time for
/// every actor that has bound packs. <c>null</c> when no actor uses packs (v1/v2 snapshots read as null).
/// The embedded actor profiles already carry the composed prompt in their <c>SystemPrompt</c>; this
/// field preserves the role prompt and provenance for the audit UI. Trailing-optional.
/// </param>
/// <param name="CrewDomain">
/// Domain the crew ran in, used for model-matrix assignment (D-066). Audit field; additive without a schema
/// version bump — snapshots created before D-066 read as <c>null</c>.
/// </param>
public sealed record CrewSnapshot(
    int SchemaVersion,
    string? TemplateName,
    ExecutorProfile Executor,
    IReadOnlyList<ReviewerProfile> Reviewers,
    EvaluationStrategy EvaluationStrategy,
    ConvergencePolicyOverride? ConvergenceOverride,
    IReadOnlyList<Advisors.AdvisorProfile> Advisors,
    IReadOnlyList<GroundingProviderProfile>? GroundingProviders = null,
    IReadOnlyList<FinalizerProfile>? Finalizers = null,
    bool RunFinalizersOnMaxAttempts = false,
    IReadOnlyDictionary<string, IReadOnlyList<ToolDefinition>>? ToolBindings = null,
    IReadOnlyList<ActorPromptComposition>? PromptCompositions = null,
    Models.ModelDomain? CrewDomain = null)
{
    /// <summary>Current snapshot schema version. Bump when the format changes incompatibly.</summary>
    public const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions DeserializeOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Tolerant read for display purposes: returns <c>null</c> if <paramref name="json"/> is null, empty, or cannot
    /// be parsed. Never use it to decide which crew a run executes; use <see cref="DeserializeForRun"/>.
    /// </summary>
    public static CrewSnapshot? Deserialize(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<CrewSnapshot>(json, DeserializeOpts);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="json"/> passes <see cref="DeserializeForRun"/>, i.e. a run could start from it.</summary>
    public static bool IsUsableForRun(string? json)
    {
        try
        {
            DeserializeForRun(json);
            return true;
        }
        catch (InvalidCrewSnapshotException)
        {
            return false;
        }
    }

    /// <summary>
    /// Strict read for executing a run. Throws <see cref="InvalidCrewSnapshotException"/> with a user-facing
    /// reason when the snapshot is missing, unreadable, of an unknown schema version or lacks the executor or the
    /// reviewer and advisor lists. There is no fallback crew.
    /// </summary>
    public static CrewSnapshot DeserializeForRun(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidCrewSnapshotException("This task has no saved setup, so it cannot start. Please submit it again.");

        CrewSnapshot? snapshot;
        try
        {
            snapshot = JsonSerializer.Deserialize<CrewSnapshot>(json, DeserializeOpts);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            throw new InvalidCrewSnapshotException(
                "The saved setup of this task could not be read. Please submit it again.", ex);
        }

        if (snapshot is null)
            throw new InvalidCrewSnapshotException("The saved setup of this task is empty. Please submit it again.");
        if (snapshot.SchemaVersion is < 1 or > CurrentSchemaVersion)
            throw new InvalidCrewSnapshotException(
                $"The saved setup of this task uses an unknown format (version {snapshot.SchemaVersion}). Please submit it again.");

        var missing = new List<string>();
        if (snapshot.Executor is null)
            missing.Add("writer");
        if (snapshot.Reviewers is null)
            missing.Add("checks");
        if (snapshot.Advisors is null)
            missing.Add("advisors");
        if (missing.Count > 0)
            throw new InvalidCrewSnapshotException(
                $"The saved setup of this task is incomplete (missing: {string.Join(", ", missing)}). Please submit it again.");

        return snapshot;
    }
}
