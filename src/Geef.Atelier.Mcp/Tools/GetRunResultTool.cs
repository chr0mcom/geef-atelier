using System.ComponentModel;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class GetRunResultTool
{
    [McpServerTool, Description("Gets the final result text of a completed run.")]
    public static async Task<RunResultDto?> GetRunResult(
        IRunService runService,
        [Description("The run ID (GUID).")] string runId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out var guid)) return null;
        var run = await runService.GetRunAsync(guid, requestingUsername: null, cancellationToken);
        return run is null ? null : new RunResultDto(
            run.Id.ToString(),
            run.Status.ToString(),
            run.FinalText,
            run.ErrorMessage);
    }
}
