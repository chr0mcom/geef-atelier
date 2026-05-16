using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class GetRunStatusTool
{
    [McpServerTool, Description("Gets the current status of a run by its ID.")]
    public static async Task<RunStatusDto?> GetRunStatus(
        IRunService runService,
        ICurrentUserService currentUser,
        [Description("The run ID (GUID).")] string runId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out var guid)) return null;
        var requestingUsername = currentUser.IsAdmin ? null : currentUser.Username;
        var run = await runService.GetRunAsync(guid, requestingUsername, cancellationToken);
        return run is null ? null : new RunStatusDto(run.Id.ToString(), run.Status.ToString(), run.ErrorMessage);
    }
}
