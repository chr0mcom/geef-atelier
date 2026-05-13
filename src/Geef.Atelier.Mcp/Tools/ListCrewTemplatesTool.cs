using System.ComponentModel;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class ListCrewTemplatesTool
{
    [McpServerTool, Description("Lists all available crew templates (system built-ins and user-created custom templates).")]
    public static async Task<IReadOnlyList<CrewTemplateDto>> ListCrewTemplates(
        ICrewService crewService,
        [Description("Whether to include system templates. Defaults to true.")] bool includeSystem = true,
        CancellationToken cancellationToken = default)
    {
        var templates = await crewService.ListCrewTemplatesAsync(includeSystem, cancellationToken);
        return templates
            .Select(t => new CrewTemplateDto(
                t.Name, t.DisplayName, t.Description,
                t.ExecutorProfileName,
                t.ReviewerProfileNames,
                t.EvaluationStrategy.ToString(),
                t.IsSystem))
            .ToList();
    }
}
