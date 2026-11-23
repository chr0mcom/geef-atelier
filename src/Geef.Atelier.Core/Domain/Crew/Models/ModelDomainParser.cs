namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>
/// Exact parsing of <see cref="ModelDomain"/> names. The crew composer states the matrix row explicitly as
/// <c>model_domain</c> (D-066/12); there is deliberately no keyword heuristic, because every word list produced the next
/// class of false positives (and paid reviewers with them).
/// </summary>
public static class ModelDomainParser
{
    /// <summary>Parses an exact enum name (trimmed, case-insensitive); returns <c>null</c> for anything else.</summary>
    public static ModelDomain? TryParseExact(string? raw) => EnumNames.ParseExact<ModelDomain>(raw);
}
