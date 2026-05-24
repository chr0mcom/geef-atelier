using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class GetRunDetailsTool
{
    [McpServerTool, Description("Gets detailed information about a run including all iterations and their artifact text.")]
    public static async Task<RunDetailsDto?> GetRunDetails(
        IRunService runService,
        ICurrentUserService currentUser,
        [Description("The run ID (GUID).")] string runId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out var guid)) return null;
        var requestingUsername = currentUser.IsAdmin ? null : currentUser.Username;
        var details = await runService.GetRunDetailsAsync(guid, requestingUsername, cancellationToken);
        if (details is null) return null;
        var iterations = details.Iterations
            .Select(i => new IterationDto(
                i.Iteration.IterationNumber,
                i.Iteration.ArtifactText,
                i.Findings.Select(f => new FindingDto(
                    f.ReviewerName,
                    f.Severity.ToString(),
                    f.Message)).ToList()))
            .ToList();
        return new RunDetailsDto(
            details.Run.Id.ToString(),
            details.Run.Status.ToString(),
            details.Run.CreatedAt,
            details.Run.CreatedByUser,
            details.Run.BriefingText,
            details.Run.FinalText,
            details.Run.ErrorMessage,
            iterations);
    }
}
