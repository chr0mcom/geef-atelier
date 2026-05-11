using System.ComponentModel;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class SubmitRequestTool
{
    [McpServerTool, Description("Submits a new run request with a briefing text and optional JSON configuration.")]
    public static async Task<RunStatusDto> SubmitRequest(
        IRunService runService,
        [Description("The briefing text describing the task for the AI orchestrator.")] string briefingText,
        [Description("Optional JSON configuration object. Defaults to '{}'.")] string? configJson = null,
        CancellationToken cancellationToken = default)
    {
        var runId = await runService.SubmitRunAsync(
            briefingText,
            configJson ?? "{}",
            createdByUser: "mcp-client",
            cancellationToken: cancellationToken);
        return new RunStatusDto(runId.ToString(), "Pending", null);
    }
}
