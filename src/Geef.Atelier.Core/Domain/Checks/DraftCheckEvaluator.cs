namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>Checks a draft against <see cref="OrderConstraints"/> without an AI model.</summary>
public static class DraftCheckEvaluator
{
    /// <summary>Runs every check that applies (empty text and preamble always) and returns one result per check.</summary>
    public static IReadOnlyList<DraftCheckResult> Evaluate(string draft, OrderConstraints constraints) =>
        throw new NotImplementedException();

    /// <summary>Finds placeholders in square brackets, ignoring Markdown links, footnotes and check boxes.</summary>
    public static IReadOnlyList<string> FindPlaceholders(string draft) =>
        throw new NotImplementedException();
}
