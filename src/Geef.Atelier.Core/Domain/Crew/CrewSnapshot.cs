using System.Text.Json;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Tools;

namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Frozen, fully-dereferenced snapshot of the crew configuration used for a single run.
/// Persisted as JSONB on <c>RunEntity.CrewSnapshot</c> so that runs remain reproducible even
/// after referenced profiles are renamed or deleted.
/// </summary>
/// <param name="SchemaVersion">
/// Format version. Increment when the snapshot schema changes; deserialisation throws on
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
    IReadOnlyDictionary<string, IReadOnlyList<ToolDefinition>>? ToolBindings = null)
{
    /// <summary>Current snapshot schema version. Bump when the format changes incompatibly.</summary>
    public const int CurrentSchemaVersion = 2;

    private static readonly JsonSerializerOptions DeserializeOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Attempts to deserialize a <see cref="CrewSnapshot"/> from a JSON string.
    /// Returns <c>null</c> if <paramref name="json"/> is null, empty, or cannot be parsed.
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
}
