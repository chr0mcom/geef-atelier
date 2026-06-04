namespace Geef.Atelier.Application.Composition;

/// <summary>
/// Represents a single structural issue found when validating a Crew-Spec JSON document.
/// </summary>
/// <param name="Field">Dot-separated path of the offending field (e.g. <c>"executor"</c>, <c>"reviewers[0].reuse"</c>).</param>
/// <param name="Message">Human-readable description of the problem.</param>
/// <param name="IsCritical">
/// <see langword="true"/> when the issue prevents the spec from being executed at all (missing required
/// sections, unresolvable references, invalid JSON). <see langword="false"/> for non-blocking warnings.
/// </param>
public sealed record CrewSpecValidationIssue(
    string Field,
    string Message,
    bool IsCritical);

/// <summary>
/// Validates a Crew-Spec JSON string deterministically without calling any LLM.
/// Checks JSON structure, required fields, profile-catalog membership, and provider/model availability.
/// </summary>
public interface ICrewSpecValidator
{
    /// <summary>
    /// Validates <paramref name="specJson"/> and returns all detected issues.
    /// An empty list means the spec is structurally sound and all references resolved.
    /// </summary>
    /// <param name="specJson">The raw JSON of a Crew-Spec artifact produced during composition.</param>
    /// <param name="cancellationToken">Propagated to any async catalog lookups.</param>
    /// <returns>
    /// A list of <see cref="CrewSpecValidationIssue"/> instances, ordered roughly by severity.
    /// Returns an empty list when the spec is valid.
    /// </returns>
    Task<IReadOnlyList<CrewSpecValidationIssue>> ValidateAsync(
        string specJson,
        CancellationToken cancellationToken = default);
}
