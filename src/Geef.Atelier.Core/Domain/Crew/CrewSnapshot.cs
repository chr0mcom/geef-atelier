using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Frozen, fully-dereferenced snapshot of the setup used for a single run, persisted as JSON in <c>RunEntity.CrewSnapshot</c> so that a
/// run stays reproducible. Snapshots of the removed ways of working (D-091) name more fields (advisors with content, grounding sources,
/// finishers, tool bindings, pack compositions); they are read here without those fields and are never run again.
/// </summary>
/// <param name="SchemaVersion">
/// Format version. Increment when the snapshot schema changes; <see cref="DeserializeForRun"/> throws on unknown versions to surface
/// format drift early.
/// </param>
/// <param name="TemplateName">Name of the code-only template the snapshot was built from.</param>
/// <param name="Executor">Embedded writer profile (full content, not just a reference).</param>
/// <param name="Reviewers">Embedded check profiles in the order required by the evaluation strategy.</param>
/// <param name="EvaluationStrategy">Strategy used for the checks.</param>
/// <param name="ConvergenceOverride">Convergence-policy overrides applied for this run, if any.</param>
/// <param name="Advisors">
/// Always empty for new runs. Kept in the stored format because images before stage 8d refuse a snapshot without the list; snapshots
/// of the removed review loop may carry advisor entries, which are kept as they are and never used.
/// </param>
/// <param name="CrewDomain">Domain the setup ran in, used for model-matrix assignment (D-066); <c>null</c> for snapshots before D-066.</param>
public sealed record CrewSnapshot(
    int SchemaVersion,
    string? TemplateName,
    ExecutorProfile Executor,
    IReadOnlyList<ReviewerProfile> Reviewers,
    EvaluationStrategy EvaluationStrategy,
    ConvergencePolicyOverride? ConvergenceOverride,
    IReadOnlyList<JsonElement> Advisors,
    Models.ModelDomain? CrewDomain = null)
{
    /// <summary>Current snapshot schema version. Bump when the format changes incompatibly.</summary>
    public const int CurrentSchemaVersion = 3;

    private static readonly JsonSerializerOptions DeserializeOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Tolerant read for display purposes: returns <c>null</c> if <paramref name="json"/> is null, empty, or cannot
    /// be parsed. Never use it to decide which setup a run executes; use <see cref="DeserializeForRun"/>.
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
    /// reason when the snapshot is missing, unreadable, of an unknown schema version or lacks the writer or the
    /// list of checks or advisors. There is no fallback setup.
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
