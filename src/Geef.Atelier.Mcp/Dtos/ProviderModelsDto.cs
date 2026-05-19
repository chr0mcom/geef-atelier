namespace Geef.Atelier.Mcp.Dtos;

public sealed record ProviderModelsDto(
    string ProviderName,
    IReadOnlyList<string> Models);
