namespace Geef.Atelier.Mcp.Dtos;

public sealed record ProviderDto(
    string Name,
    string DisplayName,
    string Description,
    string Type,
    bool IsSystem,
    bool IsActive);
