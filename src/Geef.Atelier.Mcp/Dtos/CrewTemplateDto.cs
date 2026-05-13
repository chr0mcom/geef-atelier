namespace Geef.Atelier.Mcp.Dtos;

public sealed record CrewTemplateDto(
    string Name,
    string DisplayName,
    string Description,
    string ExecutorProfileName,
    IReadOnlyList<string> ReviewerProfileNames,
    string EvaluationStrategy,
    bool IsSystem);
