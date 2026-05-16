using System.ComponentModel;
using Geef.Atelier.Application.Runs;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class CancelRunTool
{
    [McpServerTool, Description("Cancels a running or pending run.")]
    public static async Task<bool> CancelRun(
        IRunService runService,
        [Description("The run ID (GUID).")] string runId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out var guid)) return false;
        return await runService.CancelRunAsync(guid, requestingUsername: null, cancellationToken);
    }
}
