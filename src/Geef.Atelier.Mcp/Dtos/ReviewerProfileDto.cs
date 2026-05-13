namespace Geef.Atelier.Mcp.Dtos;

public sealed record ReviewerProfileDto(
    string Name,
    string DisplayName,
    string Description,
    string Provider,
    string Model,
    int? MaxTokens,
    bool IsSystem);
