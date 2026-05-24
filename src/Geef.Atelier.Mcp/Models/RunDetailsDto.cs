namespace Geef.Atelier.Mcp.Models;

public sealed record FindingDto(
    string ReviewerName,
    string Severity,
    string Message);

public sealed record IterationDto(
    int IterationNumber,
    string? ArtifactText,
    IReadOnlyList<FindingDto> Findings);

public sealed record RunDetailsDto(
    string RunId,
    string Status,
    DateTimeOffset CreatedAt,
    string? CreatedByUser,
    string BriefingText,
    string? FinalText,
    string? ErrorMessage,
    IReadOnlyList<IterationDto> Iterations);
