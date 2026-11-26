using Geef.Atelier.Application.Sources;
using Geef.Atelier.Mcp;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary>
/// <c>prepare_attachment</c>: a command with the token only in a header, a link without a secret, the owner from the sign-in,
/// and texts that tell an assistant acting for a layperson what to do when it cannot run the command.
/// </summary>
public sealed class PrepareAttachmentToolTests
{
    private const string Token = "Tq3sV9xZbN0aLc7YdPe2RfGh4JkWmUo8IiQvXy1-_zA";

    private readonly FakeUploadTickets _uploads = new();
    private readonly FakeUser _alice = new("alice");

    [Fact]
    public async Task The_answer_has_the_command_with_the_token_only_in_the_header_and_a_link_without_secret()
    {
        var id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        var now = DateTimeOffset.UtcNow;
        _uploads.NextPrepared = new PreparedUpload(id, Token, now.AddMinutes(15), now.AddHours(24), 26_214_400);

        var dto = await PrepareAttachmentTool.PrepareAttachment(_uploads, _alice, OrderToolTestData.OAuth("https://atelier.example/"), "Angebot.pdf", 210_000);

        Assert.Equal(("alice", "Angebot.pdf", (long?)210_000), Assert.Single(_uploads.Prepared));
        Assert.Equal(id.ToString(), dto.UploadId);
        Assert.Equal(
            "curl --fail-with-body -sS --globoff -T '<path to the file>' -H 'X-Upload-Token: " + Token + "' https://atelier.example/api/uploads/0f8fad5b-d9cb-469f-a165-70867728950e",
            dto.CurlCommand);
        Assert.Equal("https://atelier.example/u/0f8fad5b-d9cb-469f-a165-70867728950e", dto.UploadPage);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(dto.CurlCommand, Token));
        Assert.DoesNotContain(Token, dto.CurlCommand.Split(' ').Last(), StringComparison.Ordinal);
        Assert.DoesNotContain(Token, dto.UploadPage, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, dto.Next, StringComparison.Ordinal);
        Assert.Equal(now.AddMinutes(15), dto.TokenExpiresAt);
        Assert.Equal(now.AddHours(24), dto.PageExpiresAt);
        Assert.Equal(26_214_400, dto.MaxBytes);
        Assert.Equal(
            "Run curlCommand with the path of the file in single quotes; for a path with a quote, a line break or another unusual character, " +
            "copy it under a plain name and upload the copy in ONE shell command as the description of prepare_attachment says, or use the link. " +
            "If the answer has usable false, tell the person its reason word for word and do not upload the same file again. " +
            "If the command is not possible or fails without an answer from Atelier, give the person this link word for word: " +
            "https://atelier.example/u/0f8fad5b-d9cb-469f-a165-70867728950e. Then call submit_order with uploadIds.",
            dto.Next);
    }

    [Fact]
    public async Task A_refusal_of_the_service_reaches_the_assistant_word_for_word()
    {
        _uploads.PrepareFailure = new UploadTicketException(UploadTicketLines.TooManyTickets);

        var ex = await Assert.ThrowsAnyAsync<McpException>(() =>
            PrepareAttachmentTool.PrepareAttachment(_uploads, _alice, OrderToolTestData.OAuth(), "Angebot.pdf"));

        Assert.Equal("Too many open uploads. Use or let the earlier ones expire first.", ex.Message);
    }

    [Fact]
    public async Task While_tickets_are_off_nothing_is_prepared()
    {
        _uploads.Enabled = false;

        var ex = await Assert.ThrowsAnyAsync<McpException>(() =>
            PrepareAttachmentTool.PrepareAttachment(_uploads, _alice, OrderToolTestData.OAuth(), "Angebot.pdf"));

        Assert.Equal(UploadTicketLines.NotAvailable, ex.Message);
        Assert.Empty(_uploads.Prepared);
    }

    [Fact]
    public async Task Without_user_nothing_is_prepared()
    {
        await Assert.ThrowsAnyAsync<McpException>(() =>
            PrepareAttachmentTool.PrepareAttachment(_uploads, new FakeUser(null), OrderToolTestData.OAuth(), "Angebot.pdf"));

        Assert.Empty(_uploads.Prepared);
    }

    [Theory]
    [InlineData("abc' ; rm -rf ~ ; echo '")]
    [InlineData("short")]
    [InlineData("token with spaces and more text")]
    public async Task A_token_that_could_break_out_of_the_quotes_is_never_put_into_the_command(string token)
    {
        var now = DateTimeOffset.UtcNow;
        _uploads.NextPrepared = new PreparedUpload(Guid.NewGuid(), token, now, now, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PrepareAttachmentTool.PrepareAttachment(_uploads, _alice, OrderToolTestData.OAuth(), "Angebot.pdf"));
    }

    [Fact]
    public void The_description_says_to_run_the_command_only_when_possible_otherwise_to_give_the_link_and_never_to_claim_arrival()
    {
        var d = PrepareAttachmentTool.ToolDescription;

        Assert.Contains("If you can run shell commands and can read the file, run curlCommand", d, StringComparison.Ordinal);
        Assert.Contains("If you cannot run the command, or it fails without an answer from Atelier (for example with a network error or 403)", d, StringComparison.Ordinal);
        Assert.Contains("give the person uploadPage word for word and wait until they say the file is uploaded", d, StringComparison.Ordinal);
        Assert.Contains("Tell the person a file has arrived only when an answer from Atelier shows it", d, StringComparison.Ordinal);
        Assert.Contains("Never type, summarise or Base64-encode the file yourself.", d, StringComparison.Ordinal);
        Assert.Contains("uploadIds of submit_order", d, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_file_is_told_apart_from_a_failed_transfer_in_every_text_for_the_assistant()
    {
        foreach (var text in new[] { PrepareAttachmentTool.ToolDescription, PrepareAttachmentTool.NextFormat, McpServerInstructions.TicketFilesStep })
        {
            Assert.Contains("usable false", text, StringComparison.Ordinal);
            Assert.Contains("reason word for word", text, StringComparison.Ordinal);
            Assert.Contains("the same file again", text, StringComparison.Ordinal);
            Assert.Contains("fails without an answer from Atelier", text, StringComparison.Ordinal);
        }

        Assert.Contains("for example for HTTP 422, but still prints the answer", PrepareAttachmentTool.ToolDescription, StringComparison.Ordinal);
    }

    [Fact]
    public void The_texts_say_how_to_quote_the_path_and_what_to_do_with_an_unusual_name()
    {
        var rule = PrepareAttachmentTool.QuotingRule;

        Assert.Contains(rule, PrepareAttachmentTool.ToolDescription, StringComparison.Ordinal);
        Assert.Contains("Put the path of the file in single quotes", rule, StringComparison.Ordinal);
        Assert.Contains("do not type the name in a command", rule, StringComparison.Ordinal);
        Assert.Contains("written only with letters, digits and / . - _ * (a * in place of every other character)", rule, StringComparison.Ordinal);
        Assert.Contains("into a new empty folder under a plain name", rule, StringComparison.Ordinal);
        Assert.Contains("both in ONE shell command, never in two, because $d is empty in a later command: " + PrepareAttachmentTool.CopyAndUploadCommand, rule, StringComparison.Ordinal);
        Assert.Equal("d=$(mktemp -d) && cp -- <pattern> \"$d/upload\" && <curlCommand>; rm -rf \"$d\"", PrepareAttachmentTool.CopyAndUploadCommand);
        Assert.Contains("Replace <curlCommand> by curlCommand with \"$d/upload\" (with the double quotes) in place of '<path to the file>'; in single quotes $d would not be replaced.", rule, StringComparison.Ordinal);
        Assert.DoesNotContain("'$d", rule, StringComparison.Ordinal);
        Assert.Contains("in ONE shell command", PrepareAttachmentTool.NextFormat, StringComparison.Ordinal);
        Assert.Contains("give the person the link instead", rule, StringComparison.Ordinal);
        Assert.Contains("in single quotes", PrepareAttachmentTool.NextFormat, StringComparison.Ordinal);
        Assert.Contains("quoted for the shell as prepare_attachment describes", McpServerInstructions.TicketFilesStep, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ticket_step_of_the_instructions_says_the_same()
    {
        var step = McpServerInstructions.TicketFilesStep;

        Assert.StartsWith("2. For each file the person gives you, call prepare_attachment.", step, StringComparison.Ordinal);
        Assert.Contains("If you can run shell commands and read the file, upload it with its curlCommand", step, StringComparison.Ordinal);
        Assert.Contains("If you cannot run the command, or it fails without an answer from Atelier, give the person its uploadPage link word for word", step, StringComparison.Ordinal);
        Assert.Contains("Say that a file has arrived only when an answer from Atelier shows it.", step, StringComparison.Ordinal);
        Assert.EndsWith("\n", step, StringComparison.Ordinal);
        Assert.DoesNotContain("{", step, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("crew")]
    [InlineData("grounding")]
    [InlineData("iteration")]
    [InlineData("convergence")]
    [InlineData("runId")]
    [InlineData("token")]
    public void The_texts_for_the_assistant_speak_without_internal_words(string word)
    {
        foreach (var text in new[]
                 {
                     PrepareAttachmentTool.ToolDescription, PrepareAttachmentTool.NextFormat, PrepareAttachmentTool.FileNameDescription,
                     PrepareAttachmentTool.SizeBytesDescription, SubmitOrderTool.UploadIdsDescription, McpServerInstructions.TicketFilesStep,
                     McpAttachmentParser.RightWayWithTickets, McpServerInstructions.TicketStartStep, SubmitOrderTool.TicketToolDescription,
                     SubmitOrderTool.TicketAttachmentsDescription, UploadTicketLines.AfterRefusal, UploadTicketLines.AfterNotChecked,
                     UploadTicketLines.AfterQuota, UploadTicketLines.AfterUnavailable,
                 })
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }
}
