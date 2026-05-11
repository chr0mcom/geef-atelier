namespace Geef.Atelier.Mcp.Models;

public sealed record RunResultDto(
    string RunId,
    string Status,
    string? FinalText,
    string? ErrorMessage);
