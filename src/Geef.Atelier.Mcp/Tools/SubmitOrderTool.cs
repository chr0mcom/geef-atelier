using System.ComponentModel;
using System.Globalization;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Mcp.Forms;
using Geef.Atelier.Mcp.Models;
using Microsoft.Extensions.Options;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class SubmitOrderTool
{
    public const string ToolDescription =
        "Starts a writing order. Pass the person's request in their own words; do not rewrite or expand it. " +
        "Optionally attach files (JSON array of {filename, contentType, contentBase64}; PDF, Word, Markdown or text). " +
        "Waits a short while for Atelier to read the request. " +
        "If Atelier needs to know something only the person can tell, the answer has state needs_input and up to three short questions with answer options: " +
        "ask the person, then call answer_order. " +
        "Otherwise the order is already being written: call get_order_status with waitSeconds, then get_order_result. " +
        "The text is usually ready in 1–5 minutes (with extraCareful up to about 10).";

    /// <summary>The description while <c>Uploads:Tickets</c> is <c>On</c>: files go through <c>prepare_attachment</c> and <c>uploadIds</c>, Base64 only for very small text files.</summary>
    public const string TicketToolDescription =
        "Starts a writing order. Pass the person's request in their own words; do not rewrite or expand it. " +
        "Files: upload each file first with prepare_attachment and pass the upload IDs in uploadIds; files cannot be added to an order later. " +
        "Only very small text files (a few kilobytes) may go into attachments instead (JSON array of {filename, contentType, contentBase64}). " +
        "Waits a short while for Atelier to read the request. " +
        "If Atelier needs to know something only the person can tell, the answer has state needs_input and up to three short questions with answer options: " +
        "ask the person, then call answer_order. " +
        "Otherwise the order is already being written: call get_order_status with waitSeconds, then get_order_result. " +
        "The text is usually ready in 1–5 minutes (with extraCareful up to about 10).";

    /// <summary>Sentence added to the description while <c>Mcp:Skills</c> is <c>On</c> (D-088).</summary>
    public const string SkillSentence =
        " If you can load skills, load " + AtelierSkill.Uri + " before the first order; it explains every step.";

    /// <summary>Sentence added to the description while <c>Mcp:Elicitation</c> is <c>On</c> (D-088).</summary>
    public const string FormSentence =
        " Your app may show Atelier's questions to the person as a form during this call; if the answer still has state needs_input, ask the person as described.";

    /// <summary>
    /// The description of <c>submit_order</c>: <see cref="TicketToolDescription"/> while upload tickets are on, otherwise <see cref="ToolDescription"/>,
    /// followed by <see cref="SkillSentence"/> and <see cref="FormSentence"/> while their switches are on. With both switches off it is exactly the
    /// description from before D-088.
    /// </summary>
    public static string Describe(bool tickets, McpOptions mcp)
    {
        ArgumentNullException.ThrowIfNull(mcp);
        return (tickets ? TicketToolDescription : ToolDescription)
               + (mcp.Skills == McpSkillsMode.On ? SkillSentence : string.Empty)
               + (mcp.Elicitation == McpElicitationMode.On ? FormSentence : string.Empty);
    }

    /// <summary>Name of the parameter <c>attachments</c> in the input schema.</summary>
    public const string AttachmentsParameter = "attachments";

    /// <summary>The description of <c>attachments</c> while <c>Uploads:Tickets</c> is <c>On</c>.</summary>
    public const string TicketAttachmentsDescription =
        "Optional, only for very small text files (a few kilobytes); upload every other file with prepare_attachment and pass uploadIds instead. " +
        "A JSON array: [{\"filename\":\"notes.txt\",\"contentType\":\"text/plain\",\"contentBase64\":\"...\"}]. contentBase64 must be the complete original file. " +
        "Supported content types: application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document, text/markdown, text/plain. " +
        "Placeholders, summaries or shortened content are rejected, and so is Base64 encoded twice or content that does not match the contentType.";

    public const string UploadIdsDescription = "Upload IDs from prepare_attachment whose file was uploaded. Not file content, not file names.";

    /// <summary>Name of the parameter <c>uploadIds</c> in the input schema; it is left out of the schema while upload tickets are off.</summary>
    public const string UploadIdsParameter = "uploadIds";

    [McpServerTool, Description(ToolDescription)]
    public static async Task<OrderSubmittedDto> SubmitOrder(
        IOrderService orders,
        ISourceIntakeService intake,
        IUploadTicketService uploads,
        ISourceRefusalLog refusals,
        ICurrentUserService currentUser,
        IOptions<OrderOptions> orderOptions,
        IOptions<IntakeOptions> intakeOptions,
        IOptions<SourceOptions> sourceOptions,
        IOptions<OAuthOptions> oauth,
        [Description("The person's request, word for word as they wrote or said it. Do not summarise, translate, expand or rewrite it, and do not paste file content here.")] string request,
        [Description("Optional files as a JSON array: [{\"filename\":\"offer.pdf\",\"contentType\":\"application/pdf\",\"contentBase64\":\"...\"}]. contentBase64 must be the complete original file. Supported content types: application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document, text/markdown, text/plain. Placeholders, summaries or shortened content are rejected.")] string? attachments = null,
        [Description("True only when the person asks for extra care: slower, with several review rounds.")] bool extraCareful = false,
        [Description(UploadIdsDescription)] string[]? uploadIds = null,
        CancellationToken cancellationToken = default,
        IMcpOrderForms? forms = null,
        RequestContext<CallToolRequestParams>? context = null)
    {
        var username = currentUser.RequireUsername();
        if (string.IsNullOrWhiteSpace(request))
            throw new McpException("The request is empty. Pass the person's request in their own words.");

        var call = forms is null
            ? null
            : new OrderFormCall(context?.Server, context?.Params, username, OrderFormCall.ClientOf(context?.User),
                OrderFormStates.HashArguments(request, attachments, extraCareful, uploadIds));
        if (call is not null && await forms!.ResumeAsync(call, cancellationToken) is { } resumed)
            return resumed;

        var tickets = uploads.Enabled;
        var ids = ReadUploadIds(uploadIds, tickets);
        var parsed = await refusals.ParseAttachmentsAsync(attachments, username, tickets);
        var maxFiles = sourceOptions.Value.MaxFilesPerOrder;
        if (ids.Count > 0 && parsed.Count + ids.Count > maxFiles)
            throw new McpException(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.TooManyFilesFormat, maxFiles));

        var uploaded = await ResolveUploadsAsync(uploads, ids, username, oauth.Value, cancellationToken);
        var receipts = await intake.IntakeAttachmentsAsync(parsed, username, cancellationToken, tickets);
        IReadOnlyList<SourceReceipt> all = [.. receipts, .. uploaded];

        OrderSubmission submission;
        try
        {
            submission = await orders.SubmitAsync(
                new SubmitOrderRequest(
                    RequestText: request,
                    CreatedByUser: username,
                    Entry: OrderEntry.Mcp,
                    Sources: all.Select(r => new OrderSourceSelection(r.SourceId!.Value)).ToList(),
                    Mode: extraCareful ? OrderMode.Careful : OrderMode.Direct,
                    AllowQuestions: true),
                cancellationToken);
        }
        catch (OrderSubmissionException ex)
        {
            await intake.ReleaseNewSourcesAsync(receipts, username);
            throw new McpException(tickets && ex.WithoutPageChoice is { } withoutPages ? withoutPages : ex.Message, ex);
        }

        var wait = GetOrderStatusTool.ClampWait(intakeOptions.Value.SubmitWaitSeconds, orderOptions.Value.MaxWaitSeconds);
        if (submission.State != OrderDisplayState.Preparing || wait == 0)
            return Answer(submission.ToDto(all), submission.Clarification);

        var view = await orders.WaitForChangeAsync(
                       submission.OrderId, username, OrderDisplayState.Preparing, TimeSpan.FromSeconds(wait), cancellationToken)
                   ?? throw new McpException(McpOrderAccess.OrderNotFound);
        return Answer(view.ToSubmittedDto(submission.Notices, all), view.Clarification);

        OrderSubmittedDto Answer(OrderSubmittedDto dto, ClarificationView? clarification)
        {
            if (call is not null)
                forms!.AskWhenPossible(call, dto, clarification, submission.Notices);
            return dto;
        }
    }

    /// <summary>
    /// The distinct upload ids in the given order; none for <c>null</c> or <c>[]</c>. Refuses ids while upload tickets are off
    /// (<see cref="UploadTicketLines.NotAvailable"/>) and anything that is not an id (<see cref="UploadTicketLines.NotFoundFormat"/>).
    /// </summary>
    internal static IReadOnlyList<Guid> ReadUploadIds(string[]? uploadIds, bool tickets)
    {
        if (uploadIds is not { Length: > 0 })
            return [];
        if (!tickets)
            throw new McpException(UploadTicketLines.NotAvailable);

        var ids = new List<Guid>(uploadIds.Length);
        var seen = new HashSet<Guid>();
        foreach (var value in uploadIds)
        {
            var text = value?.Trim() ?? string.Empty;
            if (!Guid.TryParse(text, out var id))
                throw new McpException(string.Format(CultureInfo.InvariantCulture, UploadTicketLines.NotFoundFormat, text));
            if (seen.Add(id))
                ids.Add(id);
        }

        return ids;
    }

    private static async Task<IReadOnlyList<SourceReceipt>> ResolveUploadsAsync(
        IUploadTicketService uploads, IReadOnlyList<Guid> ids, string username, OAuthOptions oauth, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return [];

        IReadOnlyList<SourceReceipt> receipts;
        try
        {
            receipts = await uploads.ResolveAsync(ids, username, id => PrepareAttachmentTool.PageLink(oauth, id), cancellationToken);
        }
        catch (UploadTicketException ex)
        {
            throw new McpException(ex.Message, ex);
        }

        if (receipts.Count != ids.Count || receipts.Any(r => !r.Usable || r.SourceId is null))
            throw new InvalidOperationException("The upload tickets resolved to fewer usable files than ids were given.");
        return receipts;
    }
}
