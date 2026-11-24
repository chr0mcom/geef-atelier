using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class SubmitOrderTool
{
    public const string ToolDescription =
        "Starts a writing order. Pass the person's request in their own words; do not rewrite or expand it. " +
        "Optionally attach files (JSON array of {filename, contentType, contentBase64}; PDF, Word, Markdown or text). " +
        "Returns at once with an orderId and a receipt for each file. " +
        "The text is usually ready in 1–5 minutes (with extraCareful up to about 10): call get_order_status with waitSeconds to wait, then get_order_result.";

    [McpServerTool, Description(ToolDescription)]
    public static async Task<OrderSubmittedDto> SubmitOrder(
        IOrderService orders,
        ISourceIntakeService intake,
        ICurrentUserService currentUser,
        [Description("The person's request, word for word as they wrote or said it. Do not summarise, translate, expand or rewrite it, and do not paste file content here.")] string request,
        [Description("Optional files as a JSON array: [{\"filename\":\"offer.pdf\",\"contentType\":\"application/pdf\",\"contentBase64\":\"...\"}]. contentBase64 must be the complete original file. Supported content types: application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document, text/markdown, text/plain. Placeholders, summaries or shortened content are rejected.")] string? attachments = null,
        [Description("True only when the person asks for extra care: slower, with several review rounds.")] bool extraCareful = false,
        CancellationToken cancellationToken = default)
    {
        var username = currentUser.RequireUsername();
        if (string.IsNullOrWhiteSpace(request))
            throw new McpException("The request is empty. Pass the person's request in their own words.");

        var parsed = McpAttachmentParser.Parse(attachments);
        var receipts = await intake.IntakeAttachmentsAsync(parsed, username, cancellationToken);

        OrderSubmission submission;
        try
        {
            submission = await orders.SubmitAsync(
                new SubmitOrderRequest(
                    RequestText: request,
                    CreatedByUser: username,
                    Entry: OrderEntry.Mcp,
                    Sources: receipts.Select(r => new OrderSourceSelection(r.SourceId!.Value)).ToList(),
                    Mode: extraCareful ? OrderMode.Careful : OrderMode.Direct,
                    AllowQuestions: true),
                cancellationToken);
        }
        catch (OrderSubmissionException ex)
        {
            await intake.ReleaseNewSourcesAsync(receipts, username);
            throw new McpException(ex.Message, ex);
        }

        return submission.ToDto(receipts);
    }
}
