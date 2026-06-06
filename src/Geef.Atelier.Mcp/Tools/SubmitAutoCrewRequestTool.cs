using System.ComponentModel;
using System.Text.Json;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;
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
        "Automatically selects the best crew for a task and immediately submits the run — all in one step. " +
        "Uses the Template Studio meta-LLM to analyse the briefing and pick the optimal crew configuration. " +
        "If an existing template matches well, it is used directly. " +
        "If no good match exists, a new template is created and persisted before the run is started. " +
        "Returns the run ID, the crew that was selected, and the reasoning behind the choice. " +
        "Use this whenever the user asks for 'Auto' crew selection or when you are unsure which crew template to pick.")]
    public static async Task<AutoCrewRunStatusDto> SubmitAutoCrewRequest(
        ITemplateStudioService studioService,
        IRunService runService,
        ICurrentUserService currentUser,
        [Description("The briefing text describing the task for the AI crew.")] string briefingText,
        [Description("Optional JSON configuration object. Defaults to '{}'.")] string? configJson = null,
        [Description("Optional attachments as a JSON array: [{\"filename\":\"report.md\",\"contentType\":\"text/plain\",\"contentBase64\":\"...\"}]. Supported content types: text/markdown, text/plain, application/pdf.")] string? attachments = null,
        CancellationToken cancellationToken = default)
    {
        // Step 1 — meta-LLM analysis to find the best crew.
        var analysis = await studioService.AnalyzeAsync(briefingText, cancellationToken);

        string? templateName = null;
        bool newTemplateCreated = false;

        if (analysis.Recommendation == StudioRecommendation.UseExistingTemplate
            && analysis.MatchedExistingTemplates.Count > 0)
        {
            templateName = analysis.MatchedExistingTemplates[0].TemplateName;
        }
        else if (analysis.ProposedTemplate is not null)
        {
            // Step 2 — materialize the proposed template so submit_request can reference it by name.
            var result = await studioService.MaterializeAsync(
                analysis.Id,
                new MaterializationRequest(analysis.ProposedTemplate, analysis.ProposedNewProfiles),
                cancellationToken);

            templateName = result.CreatedTemplateName;
            newTemplateCreated = true;
        }
        // If neither path applies, templateName stays null → system default is used.

        // Step 3 — parse optional attachments.
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

        // Step 4 — submit the run.
        var runId = await runService.SubmitRunAsync(
            new SubmitRunRequest(
                BriefingText: briefingText,
                ConfigJson: configJson ?? "{}",
                CreatedByUser: currentUser.Username,
                CrewTemplateName: templateName,
                CustomCrew: null,
                Attachments: attachmentInputs),
            cancellationToken);

        return new AutoCrewRunStatusDto(
            RunId: runId.ToString(),
            Status: "Pending",
            SelectedTemplate: templateName ?? "(system default)",
            NewTemplateCreated: newTemplateCreated,
            Recommendation: analysis.Recommendation.ToString(),
            ReasoningSummary: analysis.ReasoningSummary,
            AnalysisCostEur: analysis.CostEur);
    }

    internal sealed record AttachmentDto(
        [property: System.Text.Json.Serialization.JsonRequired] string Filename,
        [property: System.Text.Json.Serialization.JsonRequired] string ContentType,
        [property: System.Text.Json.Serialization.JsonRequired] string ContentBase64);
}

public sealed record AutoCrewRunStatusDto(
    string RunId,
    string Status,
    string SelectedTemplate,
    bool NewTemplateCreated,
    string Recommendation,
    string ReasoningSummary,
    decimal? AnalysisCostEur);
