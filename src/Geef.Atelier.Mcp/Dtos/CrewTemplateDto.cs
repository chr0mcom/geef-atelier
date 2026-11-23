namespace Geef.Atelier.Mcp.Dtos;

/// <remarks><c>Domain</c>: crew domain selecting the model-matrix row (D-066); <c>null</c> means General.</remarks>
public sealed record CrewTemplateDto(
    string Name,
    string DisplayName,
    string Description,
    string ExecutorProfileName,
    IReadOnlyList<string> ReviewerProfileNames,
    string EvaluationStrategy,
    bool IsSystem,
    string? Domain = null);
