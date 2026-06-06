using System.ComponentModel;
using System.Text.Json;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class SubmitAutoCrewRequestTool
{
    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly IReadOnlySet<string> SupportedAttachmentContentTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text/markdown",
            "text/plain",
            "application/pdf",
        };

    [McpServerTool, Description(
        "Automatically composes the best crew for a task and then executes the task — all in one step. " +
        "Internally this triggers two pipeline runs: (1) a Crew-Composition run that analyses the task and " +
        "produces a validated crew specification, then (2) a normal run that executes the task with the composed crew. " +
        "Use this whenever the user asks for 'Auto' crew selection or when you are unsure which crew template to pick. " +
        "The tool returns immediately after the composition run is queued; both runs are visible in /runs.")]
    public static async Task<AutoCrewRunStatusDto> SubmitAutoCrewRequest(
        IRunService runService,
        ICurrentUserService currentUser,
        [Description("The briefing text describing the task for the AI crew.")] string briefingText,
        [Description("Optional JSON configuration object. Defaults to '{}'.")] string? configJson = null,
        [Description("When true (default), the system automatically starts the task run once crew composition is complete. Set to false to only compose the crew without executing.")] bool chainToTaskRun = true,
        [Description("Optional attachments as a JSON array: [{\"filename\":\"report.md\",\"contentType\":\"text/plain\",\"contentBase64\":\"...\"}]. Supported content types: text/markdown, text/plain, application/pdf.")] string? attachments = null,
        CancellationToken cancellationToken = default)
    {
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
                CreatedByUser: currentUser.Username,
                CrewTemplateName: null,
                CustomCrew: null,
                Attachments: attachmentInputs,
                AutoCompose: true,
                ChainToTaskRun: chainToTaskRun),
            cancellationToken);

        return new AutoCrewRunStatusDto(
            RunId: runId.ToString(),
            Status: "Pending",
            ChainToTaskRun: chainToTaskRun);
    }

    internal sealed record AttachmentDto(
        [property: System.Text.Json.Serialization.JsonRequired] string Filename,
        [property: System.Text.Json.Serialization.JsonRequired] string ContentType,
        [property: System.Text.Json.Serialization.JsonRequired] string ContentBase64);
}

public sealed record AutoCrewRunStatusDto(
    string RunId,
    string Status,
    bool ChainToTaskRun);
