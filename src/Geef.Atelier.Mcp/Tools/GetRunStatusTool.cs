using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class GetRunStatusTool
{
    [McpServerTool, Description("Gets the current status of a run by its ID, including warnings about sources, checks or models the run had to skip or replace. The field 'outcome' stays empty until the run has ended and everything is saved, including the finishing steps; wait for it rather than for the status. Outcome values: Converged (all checks passed), DeliveredWithOpenPoints (a best text was delivered with open points; this includes status Aborted after a critical finding when a text exists), Failed (no usable text, or the result could not be saved completely), Aborted (cancelled or stopped).")]
    public static async Task<RunStatusDto?> GetRunStatus(
        IRunService runService,
        ICurrentUserService currentUser,
        [Description("The run ID (GUID).")] string runId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out var guid)) return null;
        var requestingUsername = currentUser.RequestingUsername();
        var run = await runService.GetRunAsync(guid, requestingUsername, cancellationToken);
        if (run is null) return null;
        var warnings = await runService.ListRunWarningsAsync(guid, requestingUsername, cancellationToken);
        return new RunStatusDto(run.Id.ToString(), run.Status.ToString(), run.ErrorMessage,
            warnings.ToDtos(), run.Outcome?.ToString());
    }
}
