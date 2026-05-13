using System.ComponentModel;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Mcp.Dtos;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class ListReviewerProfilesTool
{
    [McpServerTool, Description("Lists all available reviewer profiles (system built-ins and user-created custom profiles).")]
    public static async Task<IReadOnlyList<ReviewerProfileDto>> ListReviewerProfiles(
        ICrewService crewService,
        [Description("Whether to include system profiles. Defaults to true.")] bool includeSystem = true,
        CancellationToken cancellationToken = default)
    {
        var profiles = await crewService.ListReviewerProfilesAsync(includeSystem, cancellationToken);
        return profiles
            .Select(p => new ReviewerProfileDto(
                p.Name, p.DisplayName, p.Description,
                p.Provider, p.Model, p.MaxTokens, p.IsSystem))
            .ToList();
    }
}
