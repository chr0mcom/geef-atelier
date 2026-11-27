namespace Geef.Atelier.Mcp.Dtos;

public sealed record GroundingProviderProfileDto(
    string Name,
    string DisplayName,
    string Description,
    string ProviderType,
    bool IsSystem,
    bool RefinementEnabled,
    string? RefinementMode);
