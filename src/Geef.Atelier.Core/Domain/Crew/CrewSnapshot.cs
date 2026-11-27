using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Frozen, fully-dereferenced snapshot of the setup used for a single run, persisted as JSON in <c>RunEntity.CrewSnapshot</c> so that a
/// run stays reproducible.
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
/// <param name="CrewDomain">Domain the setup ran in, used for model-matrix assignment (D-066); <c>null</c> means <c>General</c>.</param>
public sealed record CrewSnapshot(
    int SchemaVersion,
    string? TemplateName,
    ExecutorProfile Executor,
    IReadOnlyList<ReviewerProfile> Reviewers,
    EvaluationStrategy EvaluationStrategy,
    ConvergencePolicyOverride? ConvergenceOverride,
    Models.ModelDomain? CrewDomain = null)
{
    /// <summary>Current snapshot schema version. Bump when the format changes incompatibly.</summary>
    public const int CurrentSchemaVersion = 4;

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
    /// reason when the snapshot is missing, unreadable, not of <see cref="CurrentSchemaVersion"/> or lacks the writer or the
    /// list of checks. There is no fallback setup.
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
        if (snapshot.SchemaVersion != CurrentSchemaVersion)
            throw new InvalidCrewSnapshotException(
                $"The saved setup of this task uses an unknown format (version {snapshot.SchemaVersion}). Please submit it again.");

        var missing = new List<string>();
        if (snapshot.Executor is null)
            missing.Add("writer");
        if (snapshot.Reviewers is null)
            missing.Add("checks");
        if (missing.Count > 0)
            throw new InvalidCrewSnapshotException(
                $"The saved setup of this task is incomplete (missing: {string.Join(", ", missing)}). Please submit it again.");

        return snapshot;
    }
}
