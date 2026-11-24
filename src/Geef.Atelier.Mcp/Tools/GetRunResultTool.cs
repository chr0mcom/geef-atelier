using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class GetRunResultTool
{
    [McpServerTool, Description("Gets the final result text of a run, with warnings about anything the run had to skip or replace and the outcome (Converged, DeliveredWithOpenPoints, Failed, Aborted).")]
    public static async Task<RunResultDto?> GetRunResult(
        IRunService runService,
        ICurrentUserService currentUser,
        [Description("The run ID (GUID).")] string runId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out var guid)) return null;
        var requestingUsername = currentUser.IsAdmin ? null : currentUser.Username;
        var run = await runService.GetRunAsync(guid, requestingUsername, cancellationToken);
        if (run is null) return null;
        var warnings = await runService.ListRunWarningsAsync(guid, requestingUsername, cancellationToken);
        return new RunResultDto(
            run.Id.ToString(),
            run.Status.ToString(),
            run.FinalText,
            run.ErrorMessage,
            warnings.ToDtos(),
            run.Outcome?.ToString());
    }
}
