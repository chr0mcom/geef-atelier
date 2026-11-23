namespace Geef.Atelier.Mcp.Dtos;

/// <remarks><c>ModelSlot</c>: model-matrix role (D-066), <c>null</c> when the model was chosen manually. <c>ModelDomain</c>: profile-level domain override, <c>null</c> inherits the crew domain.</remarks>
public sealed record AdvisorProfileDto(
    string Name,
    string DisplayName,
    string Description,
    string Mode,
    string Trigger,
    string Provider,
    string Model,
    bool IsSystem,
    string? ModelSlot = null,
    string? ModelDomain = null);
