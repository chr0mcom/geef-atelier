namespace Geef.Atelier.Core.Domain;

public sealed record FinalizedDocument
{
    public required string Markdown { get; init; }
    public required int IterationCount { get; init; }
}
