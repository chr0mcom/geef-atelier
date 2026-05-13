using System.ComponentModel;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class ListAdvisorProfilesTool
{
    [McpServerTool, Description("Lists all available advisor profiles including system-provided and custom profiles. Advisors provide strategic consultation before or between executor passes.")]
    public static async Task<IReadOnlyList<AdvisorProfileDto>> ListAdvisorProfiles(
        ICrewService crewService,
        [Description("Whether to include system profiles. Defaults to true.")] bool includeSystem = true,
        CancellationToken cancellationToken = default)
    {
        var profiles = await crewService.ListAdvisorProfilesAsync(includeSystem, cancellationToken);
        return profiles
            .Select(p => new AdvisorProfileDto(
                p.Name, p.DisplayName, p.Description,
                p.Mode.ToString(), p.Trigger.ToString(),
                p.Provider, p.Model, p.IsSystem))
            .ToList();
    }
}
