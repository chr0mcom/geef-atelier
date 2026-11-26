using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary>
/// <c>submit_order</c> with <c>uploadIds</c> (contract 5.4): only arrived files of the caller, all or nothing, together with
/// <c>attachments</c> at most the file limit, and a fixed sentence while upload tickets are off.
/// </summary>
public sealed class SubmitOrderUploadIdsTests
{
    private const string Request = "Fasse die angehängte Datei zusammen.";

    private readonly FakeOrderService _orders = new();
    private readonly FakeSourceIntake _intake = new();
    private readonly FakeUploadTickets _uploads = new();
    private readonly FakeRefusalLog _refusals = new();

    private Task<OrderSubmittedDto> Submit(string user, string[]? uploadIds, string? attachments = null, int maxFiles = 10) =>
        SubmitOrderTool.SubmitOrder(
            _orders, _intake, _uploads, _refusals, new FakeUser(user), OrderToolTestData.Options(), OrderToolTestData.Intake(),
            OrderToolTestData.Sources(maxFiles), OrderToolTestData.OAuth(), Request, attachments: attachments, uploadIds: uploadIds);

    [Fact]
    public async Task Arrived_files_of_the_caller_go_into_the_order_with_their_receipts()
    {
        var first = _uploads.AddUsed("alice", "Angebot.pdf");
        var second = _uploads.AddUsed("alice", "Notizen.pdf");

        var dto = await Submit("alice", [first.ToString(), second.ToString().ToUpperInvariant()]);

        var resolved = Assert.Single(_uploads.Resolved);
        Assert.Equal([first, second], resolved.Ids);
        Assert.Equal("alice", resolved.Owner);
        var sources = Assert.Single(_orders.Submitted).Sources!;
        Assert.Equal([_uploads.Tickets[first].Receipt!.SourceId!.Value, _uploads.Tickets[second].Receipt!.SourceId!.Value], sources.Select(s => s.SourceId));
        Assert.Equal(["Angebot.pdf", "Notizen.pdf"], dto.Sources.Select(s => s.FileName));
        Assert.Empty(_intake.Intakes);
    }

    [Fact]
    public async Task Attachments_and_uploads_go_into_the_order_together_and_attachments_are_read_on_the_inline_way()
    {
        var upload = _uploads.AddUsed("alice", "Angebot.pdf");
        var json = Attachments.Text("notes.md", "# Notes\n\nDelivery in calendar week 12.", "text/markdown");

        var dto = await Submit("alice", [upload.ToString()], json);

        var intake = Assert.Single(_intake.Intakes);
        Assert.Equal((SourceOrigin.McpAttachment, RefusalPath.McpInline), (intake.Origin, intake.Path));
        Assert.Equal(2, Assert.Single(_orders.Submitted).Sources!.Count);
        Assert.Equal(["notes.md", "Angebot.pdf"], dto.Sources.Select(s => s.FileName));
    }

    [Fact]
    public async Task The_same_id_twice_is_used_once()
    {
        var upload = _uploads.AddUsed("alice", "Angebot.pdf");

        await Submit("alice", [upload.ToString(), $" {upload} "]);

        Assert.Equal([upload], Assert.Single(_uploads.Resolved).Ids);
    }

    [Fact]
    public async Task An_upload_of_another_person_is_not_found_and_nothing_is_started()
    {
        var own = _uploads.AddUsed("alice", "Angebot.pdf");
        var foreign = _uploads.AddUsed("bob", "Geheim.pdf");
        var json = Attachments.Text("notes.md", "# Notes\n\nDelivery in calendar week 12.", "text/markdown");

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", [own.ToString(), foreign.ToString()], json));

        Assert.Equal($"Upload {foreign} was not found. Nothing was started.", ex.Message);
        Assert.DoesNotContain("Geheim", ex.Message, StringComparison.Ordinal);
        Assert.Empty(_orders.Submitted);
        Assert.Empty(_intake.Intakes);
        Assert.Empty(_intake.Deletes);
    }

    [Fact]
    public async Task An_unknown_id_and_text_that_is_no_id_are_not_found()
    {
        var unknown = Guid.NewGuid();

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", [unknown.ToString()]));
        Assert.Equal($"Upload {unknown} was not found. Nothing was started.", ex.Message);

        ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", ["Angebot.pdf"]));
        Assert.Equal("Upload Angebot.pdf was not found. Nothing was started.", ex.Message);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task A_file_that_has_not_arrived_names_the_upload_page_and_starts_nothing()
    {
        var open = _uploads.Add("alice", FakeUploadTickets.TicketState.Open);

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", [open.ToString()]));

        Assert.Equal(
            $"The file for upload {open} has not arrived yet. If the upload command failed, give the person this link: " +
            $"https://atelier.example/u/{open}. Call submit_order again when they say it is done. Nothing was started.",
            ex.Message);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task A_file_that_is_no_longer_stored_asks_to_prepare_it_again()
    {
        var deleted = _uploads.Add("alice", FakeUploadTickets.TicketState.Deleted);

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", [deleted.ToString()]));

        Assert.Equal($"The file for upload {deleted} is no longer stored. Prepare it again with prepare_attachment. Nothing was started.", ex.Message);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task More_attachments_and_uploads_together_than_the_file_limit_are_refused_before_anything_is_read()
    {
        var ids = Enumerable.Range(0, 9).Select(_ => _uploads.AddUsed("alice", "Teil.pdf").ToString()).ToArray();
        var json = Attachments.Json(
            ("a.md", "text/markdown", "# A\n\nThe first part of the notes."u8.ToArray()),
            ("b.md", "text/markdown", "# B\n\nThe second part of the notes."u8.ToArray()));

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", ids, json));

        Assert.Equal("You can use at most 10 files per order. Nothing was started.", ex.Message);
        Assert.Empty(_uploads.Resolved);
        Assert.Empty(_intake.Intakes);
        Assert.Empty(_orders.Submitted);
    }

    [Fact]
    public async Task Exactly_the_file_limit_is_accepted()
    {
        var ids = Enumerable.Range(0, 9).Select(_ => _uploads.AddUsed("alice", "Teil.pdf").ToString()).ToArray();
        var json = Attachments.Text("a.md", "# A\n\nThe first part of the notes.", "text/markdown");

        await Submit("alice", ids, json);

        Assert.Equal(10, Assert.Single(_orders.Submitted).Sources!.Count);
    }

    [Fact]
    public async Task A_submission_error_releases_the_new_attachments_only_never_the_uploaded_file()
    {
        _orders.SubmitException = new OrderSubmissionException("Your request is too long.");
        var upload = _uploads.AddUsed("alice", "Angebot.pdf");
        var json = Attachments.Text("notes.md", "# Notes\n\nDelivery in calendar week 12.", "text/markdown");

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", [upload.ToString()], json));

        Assert.Equal("Your request is too long.", ex.Message);
        var deleted = Assert.Single(_intake.Deletes);
        Assert.NotEqual(_uploads.Tickets[upload].Receipt!.SourceId, deleted.SourceId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Files_too_long_together_never_ask_for_pages_that_cannot_be_chosen_here_while_tickets_are_on(bool tickets)
    {
        _uploads.Enabled = tickets;
        _orders.SubmitException = new OrderSubmissionException(
            "Your files are too long to use completely (700,000 characters, the limit is 600,000). Choose pages for: Angebot.pdf.",
            "Your files are too long to use completely (700,000 characters, the limit is 600,000). Pages cannot be chosen here: use fewer or shorter files, " +
            "or ask the person to start the order in the Atelier web app and choose the pages there. Nothing was started.");
        var json = Attachments.Text("notes.md", "# Notes\n\nDelivery in calendar week 12.", "text/markdown");

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", null, json));

        if (tickets)
        {
            Assert.DoesNotContain("Choose pages for", ex.Message, StringComparison.Ordinal);
            Assert.Contains("start the order in the Atelier web app and choose the pages there", ex.Message, StringComparison.Ordinal);
        }
        else
        {
            Assert.Equal("Your files are too long to use completely (700,000 characters, the limit is 600,000). Choose pages for: Angebot.pdf.", ex.Message);
        }
    }

    [Fact]
    public async Task With_tickets_an_unusable_attachment_names_prepare_attachment()
    {
        _intake.UnusableReasons["scan.pdf"] = "This PDF has no readable text. It may be a scan; scanned files are not supported yet.";
        var json = Attachments.Json(("scan.pdf", "application/pdf", Attachments.MinimalPdf));

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", null, json));

        Assert.Equal(
            "The file 'scan.pdf' cannot be used: This PDF has no readable text. It may be a scan; scanned files are not supported yet. Nothing was started. " +
            McpAttachmentRefusalTests.OnWay,
            ex.Message);
        Assert.Equal(RefusalPath.McpInline, Assert.Single(_intake.Intakes).Path);
    }

    [Fact]
    public async Task While_tickets_are_off_upload_ids_are_refused_with_the_fixed_sentence_and_nothing_is_read()
    {
        _uploads.Enabled = false;
        var upload = _uploads.AddUsed("alice", "Angebot.pdf");
        var json = Attachments.Text("notes.md", "# Notes\n\nDelivery in calendar week 12.", "text/markdown");

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", [upload.ToString()], json));

        Assert.Equal(
            "Uploading files this way is not available. Attach small files in attachments or ask the person to use the Atelier web app. Nothing was started.",
            ex.Message);
        Assert.Empty(_uploads.Resolved);
        Assert.Empty(_intake.Intakes);
        Assert.Empty(_orders.Submitted);
        Assert.Empty(_refusals.Records);
    }

    [Fact]
    public async Task While_tickets_are_off_an_empty_list_changes_nothing_and_messages_name_the_web_app()
    {
        _uploads.Enabled = false;
        _intake.UnusableReasons["scan.pdf"] = "This PDF has no readable text. It may be a scan; scanned files are not supported yet.";
        var json = Attachments.Json(("scan.pdf", "application/pdf", Attachments.MinimalPdf));

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => Submit("alice", [], json));

        Assert.EndsWith(McpAttachmentRefusalTests.OffWay, ex.Message, StringComparison.Ordinal);
        Assert.Empty(_uploads.Resolved);
    }
}
