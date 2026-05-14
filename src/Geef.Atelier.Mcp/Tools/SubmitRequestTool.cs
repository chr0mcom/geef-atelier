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

    /// <summary>Supported MIME types for run-attachment uploads.</summary>
    private static readonly IReadOnlySet<string> SupportedAttachmentContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text/markdown",
            "text/plain",
            "application/pdf",
        };

    [McpServerTool, Description("Submits a new run request with a briefing text and optional JSON configuration.")]
    public static async Task<RunStatusDto> SubmitRequest(
        IRunService runService,
        [Description("The briefing text describing the task for the AI orchestrator.")] string briefingText,
        [Description("Optional JSON configuration object. Defaults to '{}'.")] string? configJson = null,
        [Description("Name of the crew template to use (e.g. 'klassik'). Defaults to the system default when omitted.")] string? crewTemplate = null,
        [Description("Optional custom crew specification as a JSON object (CrewSpec). When supplied, crewTemplate is ignored. Supported fields: executorProfileName, reviewerProfileNames, advisorProfileNames, groundingProviderProfileNames, evaluationStrategy.")] string? customCrew = null,
        [Description("Optional attachments as a JSON array: [{\"filename\":\"report.md\",\"contentType\":\"text/plain\",\"contentBase64\":\"...\"}]. Supported content types: text/markdown, text/plain, application/pdf.")] string? attachments = null,
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

        IReadOnlyList<RunAttachmentInput>? attachmentInputs = null;
        if (attachments is not null)
        {
            AttachmentDto[]? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<AttachmentDto[]>(attachments, JsonOpts);
            }
            catch (JsonException ex)
            {
                throw new ArgumentException("attachments JSON is invalid", nameof(attachments), ex);
            }

            if (parsed is { Length: > 0 })
            {
                var inputs = new List<RunAttachmentInput>(parsed.Length);
                foreach (var dto in parsed)
                {
                    if (!SupportedAttachmentContentTypes.Contains(dto.ContentType))
                        throw new ArgumentException($"Content type '{dto.ContentType}' is not supported", nameof(attachments));

                    byte[] content;
                    try
                    {
                        content = Convert.FromBase64String(dto.ContentBase64);
                    }
                    catch (FormatException ex)
                    {
                        throw new ArgumentException($"Attachment '{dto.Filename}' has invalid base64 content", nameof(attachments), ex);
                    }

                    inputs.Add(new RunAttachmentInput(
                        Filename: dto.Filename,
                        ContentType: dto.ContentType,
                        Content: content));
                }

                attachmentInputs = inputs;
            }
        }

        var runId = await runService.SubmitRunAsync(
            new SubmitRunRequest(
                BriefingText: briefingText,
                ConfigJson: configJson ?? "{}",
                CreatedByUser: "mcp-client",
                CrewTemplateName: crewSpec is null ? crewTemplate : null,
                CustomCrew: crewSpec,
                Attachments: attachmentInputs),
            cancellationToken);

        return new RunStatusDto(runId.ToString(), "Pending", null);
    }

    /// <summary>DTO for deserializing a single attachment entry from the <c>attachments</c> JSON parameter.</summary>
    internal sealed record AttachmentDto(
        [property: System.Text.Json.Serialization.JsonRequired] string Filename,
        [property: System.Text.Json.Serialization.JsonRequired] string ContentType,
        [property: System.Text.Json.Serialization.JsonRequired] string ContentBase64);
}
