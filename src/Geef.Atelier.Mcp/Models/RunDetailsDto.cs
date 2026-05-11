namespace Geef.Atelier.Mcp.Models;

public sealed record IterationDto(
    int IterationNumber,
    string? ArtifactText);

public sealed record RunDetailsDto(
    string RunId,
    string Status,
    DateTimeOffset CreatedAt,
    string? CreatedByUser,
    string BriefingText,
    string? FinalText,
    string? ErrorMessage,
    IReadOnlyList<IterationDto> Iterations);
