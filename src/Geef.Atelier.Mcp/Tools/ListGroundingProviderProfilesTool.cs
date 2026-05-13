using System.ComponentModel;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class ListGroundingProviderProfilesTool
{
    [McpServerTool, Description("Lists all available grounding-provider profiles including system-provided and custom profiles. Grounding providers (e.g. Tavily web search) enrich a briefing with external context before the crew executes.")]
    public static async Task<IReadOnlyList<GroundingProviderProfileDto>> ListGroundingProviderProfiles(
        ICrewService crewService,
        [Description("Whether to include system profiles. Defaults to true.")] bool includeSystem = true,
        CancellationToken cancellationToken = default)
    {
        var profiles = await crewService.ListGroundingProviderProfilesAsync(includeSystem, cancellationToken);
        return profiles
            .Select(p => new GroundingProviderProfileDto(
                p.Name, p.DisplayName, p.Description,
                p.ProviderType, p.MaxQueriesPerRun, p.IsSystem))
            .ToList();
    }
}
