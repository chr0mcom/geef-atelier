namespace Geef.Atelier.Mcp.Models;

public sealed record RunStatusDto(
    string RunId,
    string Status,
    string? ErrorMessage);
