namespace Geef.Atelier.Mcp.Dtos;

public sealed record AdvisorProfileDto(
    string Name,
    string DisplayName,
    string Description,
    string Mode,
    string Trigger,
    string Provider,
    string Model,
    bool IsSystem);
