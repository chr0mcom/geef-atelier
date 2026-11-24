using System.ComponentModel;
using System.Text.Json;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Models;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

/// <summary>
/// The older entry point, kept as a shell over <see cref="IOrderService"/>: it creates an order without follow-up questions
/// and still answers with the run id. Without a crew template or custom crew it uses <c>Orders:LegacySubmitDefaultMode</c>.
/// </summary>
[McpServerToolType]
public static class SubmitRequestTool
{
    public const string ToolDescription =
        "Older interface: submits a writing task and returns its runId (use get_run_status and get_run_result) plus the orderId " +
        "(also usable with get_order_status and get_order_result). Prefer submit_order for new work. " +
        "Never asks follow-up questions. Without crewTemplate or customCrew it uses the standard direct path; " +
        "with them it uses the slower path with several review rounds and the given team.";

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    [McpServerTool, Description(ToolDescription)]
    public static async Task<RunStatusDto> SubmitRequest(
        IOrderService orders,
        ISourceIntakeService intake,
        ICurrentUserService currentUser,
        IOptions<OrderOptions> options,
        [Description("The task in the person's own words.")] string briefingText,
        [Description("Optional JSON object, kept for compatibility; it no longer changes anything. Invalid JSON is rejected.")] string? configJson = null,
        [Description("Name of the crew template to use (e.g. 'klassik'). When omitted, the standard direct path is used.")] string? crewTemplate = null,
        [Description("Optional custom crew specification as a JSON object (CrewSpec). When supplied, crewTemplate is ignored. Supported fields: executorProfileName (required), reviewerProfileNames, advisorProfileNames, groundingProviderNames, evaluationStrategy. Invalid JSON is rejected.")] string? customCrew = null,
        [Description("Optional attachments as a JSON array: [{\"filename\":\"report.md\",\"contentType\":\"text/plain\",\"contentBase64\":\"...\"}]. contentBase64 must be the complete original file. Supported content types: application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document, text/markdown, text/plain.")] string? attachments = null,
        CancellationToken cancellationToken = default)
    {
        var username = currentUser.RequireUsername();
        if (string.IsNullOrWhiteSpace(briefingText))
            throw new McpException("briefingText is empty. Pass the task in the person's own words.");
        EnsureValidConfigJson(configJson);
        var crewSpec = ParseCustomCrew(customCrew);
        var template = crewSpec is null && !string.IsNullOrWhiteSpace(crewTemplate) ? crewTemplate.Trim() : null;
        var mode = crewSpec is not null || template is not null ? OrderMode.Careful : options.Value.LegacySubmitDefaultMode;

        var parsed = McpAttachmentParser.Parse(attachments);
        var receipts = await intake.IntakeAttachmentsAsync(parsed, username, cancellationToken);

        OrderSubmission submission;
        try
        {
            submission = await orders.SubmitAsync(
                new SubmitOrderRequest(
                    RequestText: briefingText,
                    CreatedByUser: username,
                    Entry: OrderEntry.Mcp,
                    Sources: receipts.Select(r => new OrderSourceSelection(r.SourceId!.Value)).ToList(),
                    Mode: mode,
                    CrewTemplateName: template,
                    CustomCrew: crewSpec,
                    AllowQuestions: false),
                cancellationToken);
        }
        catch (OrderSubmissionException ex)
        {
            await intake.ReleaseNewSourcesAsync(receipts, username);
            throw new McpException(ex.Message, ex);
        }

        if (submission.RunId is not { } runId)
            throw new McpException(
                $"The order {submission.OrderId} was created, but no task was started. Call get_order_status with this orderId.");

        return new RunStatusDto(runId.ToString(), "Pending", null, OrderId: submission.OrderId.ToString());
    }

    /// <summary>Parses <paramref name="customCrew"/>; <c>null</c> when it is blank. Invalid JSON or a spec without executor is rejected.</summary>
    internal static CrewSpec? ParseCustomCrew(string? customCrew)
    {
        if (string.IsNullOrWhiteSpace(customCrew))
            return null;

        CrewSpec? spec;
        try
        {
            spec = JsonSerializer.Deserialize<CrewSpec>(customCrew, JsonOpts);
        }
        catch (JsonException ex)
        {
            throw new McpException(
                $"customCrew is not valid JSON ({ex.Message}). Pass a JSON object such as " +
                "{\"executorProfileName\":\"default-executor\",\"reviewerProfileNames\":[\"briefing-fidelity\"]}, or leave it out.", ex);
        }

        if (spec is null || string.IsNullOrWhiteSpace(spec.ExecutorProfileName))
            throw new McpException("customCrew must be a JSON object with at least executorProfileName, or be left out.");

        return spec.ReviewerProfileNames is null ? spec with { ReviewerProfileNames = [] } : spec;
    }

    private static void EnsureValidConfigJson(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson))
            return;
        try
        {
            using var _ = JsonDocument.Parse(configJson);
        }
        catch (JsonException ex)
        {
            throw new McpException("configJson must be valid JSON, or be left out.", ex);
        }
    }
}
