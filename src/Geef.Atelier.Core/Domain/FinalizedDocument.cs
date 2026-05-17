namespace Geef.Atelier.Core.Domain;

/// <summary>
/// The finalized pipeline output: the converged Markdown text and the number of
/// evaluation iterations it took. Produced by the Finalize phase.
/// </summary>
public sealed record FinalizedDocument
{
    /// <summary>The final converged document in Markdown.</summary>
    public required string Markdown { get; init; }

    /// <summary>Number of execution/evaluation iterations performed.</summary>
    public required int IterationCount { get; init; }
}
