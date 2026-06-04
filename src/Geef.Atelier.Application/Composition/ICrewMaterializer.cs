namespace Geef.Atelier.Application.Composition;

/// <summary>
/// Result returned by <see cref="ICrewMaterializer.MaterializeAsync"/> describing what was created or reused.
/// </summary>
/// <param name="TemplateName">Name of the crew template that was materialized or reused.</param>
/// <param name="WasDuplicate">
/// <see langword="true"/> when an existing crew template was selected instead of creating a new one
/// (either by <c>ExistingTemplate</c> mode or by crew-level similarity dedup).
/// </param>
/// <param name="Warnings">Non-critical advisory messages produced during materialization.</param>
public sealed record MaterializeCrewResult(
    string TemplateName,
    bool WasDuplicate,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Parses and materializes a Crew-Spec JSON artifact into real database entities:
/// profiles (executor, reviewers, advisors, grounding providers, finalizers) and a crew template.
/// </summary>
public interface ICrewMaterializer
{
    /// <summary>
    /// Materializes the given <paramref name="specJson"/> into persistent crew entities.
    /// </summary>
    /// <param name="specJson">Raw Crew-Spec JSON produced by the composition meta-LLM.</param>
    /// <param name="sourceRunId">ID of the composition run that produced the spec; used for logging and dedup.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="MaterializeCrewResult"/> describing the outcome.</returns>
    /// <exception cref="System.Text.Json.JsonException">Thrown when <paramref name="specJson"/> cannot be parsed.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the spec contains critical validation issues.</exception>
    Task<MaterializeCrewResult> MaterializeAsync(
        string specJson,
        Guid sourceRunId,
        CancellationToken cancellationToken = default);
}
