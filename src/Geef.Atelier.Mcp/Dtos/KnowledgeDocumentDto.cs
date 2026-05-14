namespace Geef.Atelier.Mcp.Dtos;

public sealed record KnowledgeDocumentDto(
    Guid Id,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    int ChunkCount,
    decimal? IndexingCostEur,
    DateTimeOffset CreatedAt);
