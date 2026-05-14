using System.ComponentModel;
using System.Text.Json;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class SubmitRequestTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool, Description("Submits a new run request with a briefing text and optional JSON configuration.")]
    public static async Task<RunStatusDto> SubmitRequest(
        IRunService runService,
        [Description("The briefing text describing the task for the AI orchestrator.")] string briefingText,
        [Description("Optional JSON configuration object. Defaults to '{}'.")] string? configJson = null,
        [Description("Name of the crew template to use (e.g. 'klassik'). Defaults to the system default when omitted.")] string? crewTemplate = null,
        [Description("Optional custom crew specification as a JSON object (CrewSpec). When supplied, crewTemplate is ignored. Supported fields: executorProfileName, reviewerProfileNames, advisorProfileNames, groundingProviderProfileNames, evaluationStrategy.")] string? customCrew = null,
        CancellationToken cancellationToken = default)
    {
        CrewSpec? crewSpec = null;
        if (!string.IsNullOrWhiteSpace(customCrew))
        {
            try
            {
                crewSpec = JsonSerializer.Deserialize<CrewSpec>(customCrew, JsonOpts);
            }
            catch (JsonException)
            {
                // Malformed JSON — ignore and fall through to template-based path.
            }
        }

        var runId = await runService.SubmitRunAsync(
            new SubmitRunRequest(
                BriefingText: briefingText,
                ConfigJson: configJson ?? "{}",
                CreatedByUser: "mcp-client",
                CrewTemplateName: crewSpec is null ? crewTemplate : null,
                CustomCrew: crewSpec),
            cancellationToken);

        return new RunStatusDto(runId.ToString(), "Pending", null);
    }
}
