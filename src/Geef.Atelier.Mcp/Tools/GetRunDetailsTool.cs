using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
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
        var requestingUsername = currentUser.RequestingUsername();
        var details = await runService.GetRunDetailsAsync(guid, requestingUsername, cancellationToken);
        if (details is null) return null;
        var iterations = details.Iterations
            .Select(i => new IterationDto(
                i.Iteration.IterationNumber,
                i.Iteration.ArtifactText,
                ProfileCopies.ForViewer(i.Findings, details.Run.BriefingText, details.Run.CreatedByUser, currentUser.Username).Select(f => new FindingDto(
                    f.ReviewerName,
                    f.Severity.ToString(),
                    f.Message)).ToList()))
            .ToList();
        return new RunDetailsDto(
            details.Run.Id.ToString(),
            details.Run.Status.ToString(),
            details.Run.CreatedAt,
            details.Run.CreatedByUser,
            BriefingSections.ForViewer(details.Run.BriefingText, details.Run.CreatedByUser, currentUser.Username),
            details.Run.FinalText,
            details.Run.ErrorMessage,
            iterations);
    }
}
