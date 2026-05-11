namespace Geef.Atelier.Mcp.Models;

public sealed record RunSummaryDto(
    string RunId,
    string Status,
    DateTimeOffset CreatedAt,
    string? CreatedByUser);
