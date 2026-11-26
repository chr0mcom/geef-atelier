using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Orders;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class SubmitAutoCrewRequestTool
{
    [McpServerTool, Description(
        "Automatically composes the best crew for a task and then executes the task — all in one step. " +
        "Internally this triggers two pipeline runs: (1) a Crew-Composition run that analyses the task and " +
        "produces a validated crew specification, then (2) a normal run that executes the task with the composed crew. " +
        "Use this whenever the user asks for 'Auto' crew selection or when you are unsure which crew template to pick. " +
        "The tool returns immediately after the composition run is queued; both runs are visible in /runs.")]
    public static async Task<AutoCrewRunStatusDto> SubmitAutoCrewRequest(
        IRunService runService,
        ISourceRefusalLog refusals,
        ICurrentUserService currentUser,
        [Description("The briefing text describing the task for the AI crew.")] string briefingText,
        [Description("Optional JSON configuration object. Defaults to '{}'.")] string? configJson = null,
        [Description("When true (default), the system automatically starts the task run once crew composition is complete. Set to false to only compose the crew without executing.")] bool chainToTaskRun = true,
        [Description("Optional attachments as a JSON array: [{\"filename\":\"report.md\",\"contentType\":\"text/plain\",\"contentBase64\":\"...\"}]. Supported content types: text/markdown, text/plain, application/pdf.")] string? attachments = null,
        CancellationToken cancellationToken = default)
    {
        var attachmentInputs = ToLegacyAttachments(await refusals.ParseAttachmentsAsync(attachments, currentUser.Username));

        var runId = await runService.SubmitRunAsync(
            new SubmitRunRequest(
                BriefingText: briefingText,
                ConfigJson: configJson ?? "{}",
                CreatedByUser: currentUser.Username,
                CrewTemplateName: null,
                CustomCrew: null,
                Attachments: attachmentInputs,
                AutoCompose: true,
                ChainToTaskRun: chainToTaskRun,
                Entry: OrderEntry.Mcp),
            cancellationToken);

        return new AutoCrewRunStatusDto(
            RunId: runId.ToString(),
            Status: "Pending",
            ChainToTaskRun: chainToTaskRun);
    }

    private static IReadOnlyList<RunAttachmentInput>? ToLegacyAttachments(
        IReadOnlyList<(string FileName, string ContentType, byte[] Content)> attachments)
    {
        if (attachments.Count == 0)
            return null;

        var docx = attachments.FirstOrDefault(a => a.ContentType == McpAttachmentParser.DocxContentType);
        if (docx.FileName is not null)
            throw new McpException(
                $"The attachment '{docx.FileName}' is a Word document, which this tool cannot read. Use submit_order, or attach it as PDF, Markdown or text.");

        return attachments.Select(a => new RunAttachmentInput(Filename: a.FileName, ContentType: a.ContentType, Content: a.Content)).ToList();
    }
}

public sealed record AutoCrewRunStatusDto(
    string RunId,
    string Status,
    bool ChainToTaskRun);
