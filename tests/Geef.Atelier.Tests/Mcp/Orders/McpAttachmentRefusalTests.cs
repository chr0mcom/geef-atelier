using System.Text;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary>
/// Every rejection of <see cref="McpAttachmentParser"/>: the message is word for word the one before stage 6 (the expected texts
/// are literals, not the constants), with the last sentence about <c>prepare_attachment</c> only while upload tickets are on; and
/// every rejection is counted exactly once on the way <see cref="RefusalPath.McpInline"/>, without the file name or content.
/// </summary>
public sealed class McpAttachmentRefusalTests
{
    internal const string OffWay =
        "Attach the file with its original content as Base64 in contentBase64 and a matching contentType " +
        "(application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document, text/markdown or text/plain). " +
        "Never send a placeholder, a summary or shortened content instead of the file. " +
        "If the file is too large to pass as an argument, ask the person to start the order in the Atelier web app and add the file there.";

    internal const string OnWay =
        "Attach the file with its original content as Base64 in contentBase64 and a matching contentType " +
        "(application/pdf, application/vnd.openxmlformats-officedocument.wordprocessingml.document, text/markdown or text/plain). " +
        "Never send a placeholder, a summary or shortened content instead of the file. " +
        "If the file is larger than a few kilobytes, call prepare_attachment instead and upload the file with its command or link.";

    private const string Request = "Fasse die Datei kurz zusammen.";

    private readonly FakeRefusalLog _refusals = new();

    public static TheoryData<string> Names()
    {
        var data = new TheoryData<string>();
        foreach (var c in RefusalCase.All)
            data.Add(c.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void Without_tickets_the_message_is_unchanged(string name)
    {
        var c = RefusalCase.Named(name);

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(c.Json));

        Assert.True(c.MessageStart + OffWay == ex.Message, $"{name}: {ex.Message}");
    }

    [Theory]
    [MemberData(nameof(Names))]
    public void With_tickets_the_message_ends_with_prepare_attachment_instead_of_the_web_app(string name)
    {
        var c = RefusalCase.Named(name);

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(c.Json, tickets: true));

        Assert.True(c.MessageStart + OnWay == ex.Message, $"{name}: {ex.Message}");
        Assert.DoesNotContain("web app", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Names))]
    public async Task Every_rejection_is_counted_once_with_its_code_and_without_the_name(string name)
    {
        var c = RefusalCase.Named(name);

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => _refusals.ParseAttachmentsAsync(c.Json, "alice"));

        Assert.Equal(c.MessageStart + OffWay, ex.Message);
        var record = Assert.Single(_refusals.Records);
        Assert.Equal(new SourceRefusalRecord("alice", RefusalPath.McpInline, c.Code, null, c.Extension, c.DeclaredType, c.SizeBytes), record);
        if (c.Stem is not null)
            Assert.DoesNotContain(c.Stem, record.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_failing_count_does_not_change_the_answer()
    {
        _refusals.Failure = new InvalidOperationException("The database is gone.");
        var c = RefusalCase.Named("placeholder");

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => _refusals.ParseAttachmentsAsync(c.Json, "alice", tickets: true));

        Assert.Equal(c.MessageStart + OnWay, ex.Message);
        Assert.Empty(_refusals.Records);
    }

    [Fact]
    public async Task Without_a_known_user_nothing_is_counted_and_the_answer_stays()
    {
        var c = RefusalCase.Named("short");

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => _refusals.ParseAttachmentsAsync(c.Json, null));

        Assert.Equal(c.MessageStart + OffWay, ex.Message);
        Assert.Empty(_refusals.Records);
    }

    [Fact]
    public async Task Usable_attachments_are_not_counted()
    {
        var parsed = await _refusals.ParseAttachmentsAsync(Attachments.Json(("offer.pdf", "application/pdf", Attachments.MinimalPdf)), "alice");

        Assert.Single(parsed);
        Assert.Empty(_refusals.Records);
    }

    [Fact]
    public async Task Submit_order_counts_a_rejected_attachment_once_and_starts_nothing()
    {
        var orders = new FakeOrderService();
        var intake = new FakeSourceIntake();
        var c = RefusalCase.Named("placeholder");

        var ex = await Assert.ThrowsAnyAsync<McpException>(() => SubmitOrderTool.SubmitOrder(
            orders, intake, new FakeUploadTickets(enabled: false), _refusals, new FakeUser("alice"), OrderToolTestData.Options(), OrderToolTestData.Intake(),
            OrderToolTestData.Sources(), OrderToolTestData.OAuth(), Request, attachments: c.Json));

        Assert.Equal(c.MessageStart + OffWay, ex.Message);
        Assert.Equal(c.Code, Assert.Single(_refusals.Records).Code);
        Assert.Empty(intake.Intakes);
        Assert.Empty(orders.Submitted);
    }

    [Theory]
    [InlineData("application/pdf", "application/pdf")]
    [InlineData(" Application/PDF ; charset=binary", "application/pdf")]
    [InlineData("text/x-markdown", "text/x-markdown")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("see the attached contract of Max Example", null)]
    [InlineData("text/plain/extra", null)]
    [InlineData("image/png\nX-Other: 1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_declared_type_is_counted_only_when_it_looks_like_a_media_type(string? declared, string? expected) =>
        Assert.Equal(expected, McpAttachmentParser.NormalizeDeclaredType(declared));

    [Fact]
    public void A_declared_type_longer_than_one_hundred_characters_is_not_counted()
    {
        var longType = new string('a', 50) + "/" + new string('b', 50);

        Assert.Equal(101, longType.Length);
        Assert.Null(McpAttachmentParser.NormalizeDeclaredType(longType));
        Assert.Equal(longType[..^1], McpAttachmentParser.NormalizeDeclaredType(longType[..^1]));
    }
}

/// <summary>
/// A rejected <c>attachments</c> argument: the start of its message (before the sentences about the right way) and what is counted.
/// <see cref="Stem"/> is the name without extension, which must never be counted.
/// </summary>
internal sealed record RefusalCase(
    string Name, string Json, string MessageStart, string Code, string? Extension, string? DeclaredType, long? SizeBytes, string? Stem)
{
    private const string Pdf = "application/pdf";
    private const string Text = "text/plain";
    private static readonly byte[] PdfBytes = Attachments.MinimalPdf;
    private static readonly string PdfAsBase64 = Convert.ToBase64String(PdfBytes);
    private const string Placeholder = "Siehe angehängte Vollversion im Briefing-Kontext";

    public static RefusalCase Named(string name) => All.Single(c => c.Name == name);

    public static IReadOnlyList<RefusalCase> All { get; } =
    [
        new("json", "not valid json{{", "attachments must be a JSON array of objects with filename, contentType and contentBase64. ",
            SourceRefusalCodes.McpJson, null, null, null, null),
        new("filename", """[{"filename":" ","contentType":"text/plain","contentBase64":"SGVsbG8gd29ybGQgaGVsbG8="}]""", "Every attachment needs a filename. ",
            SourceRefusalCodes.McpJson, null, Text, null, null),
        new("type", Attachments.Json(("Photo.PNG", "image/png; q=1", "binary data"u8.ToArray())),
            "Content type 'image/png; q=1' of attachment 'Photo.PNG' is not supported. ",
            SourceRefusalCodes.McpUnsupportedType, "png", "image/png", null, "Photo"),
        new("no content", """[{"filename":"doc.txt","contentType":"text/plain","contentBase64":""}]""", "The attachment 'doc.txt' has no content. ",
            SourceRefusalCodes.McpEmpty, "txt", Text, null, null),
        new("base64", """[{"filename":"Angebot-Muster.pdf","contentType":"application/pdf","contentBase64":"JVBERi0xLjQKJ..."}]""",
            "The attachment 'Angebot-Muster.pdf' has content that is not valid Base64 (it may be shortened or contain a placeholder). ",
            SourceRefusalCodes.McpBase64Invalid, "pdf", Pdf, null, "Angebot-Muster"),
        new("pdf placeholder", Attachments.Json(("offer.pdf", Pdf, Encoding.UTF8.GetBytes(Placeholder))),
            "The attachment 'offer.pdf' is declared as PDF, but its content is not a PDF file (it looks like plain text, for example a placeholder). ",
            SourceRefusalCodes.McpPlaceholder, "pdf", Pdf, Encoding.UTF8.GetByteCount(Placeholder), "offer"),
        new("pdf twice", Attachments.Json(("offer.pdf", Pdf, Encoding.ASCII.GetBytes(PdfAsBase64))),
            "The attachment 'offer.pdf' is declared as PDF, but its content is not a PDF file (it looks like Base64 text, so the file was probably encoded twice). ",
            SourceRefusalCodes.McpBase64Invalid, "pdf", Pdf, PdfAsBase64.Length, "offer"),
        new("docx zip", Attachments.Json(("letter.docx", McpAttachmentParser.DocxContentType, PdfBytes)),
            "The attachment 'letter.docx' is declared as a Word document, but its content is not a .docx file (it looks like plain text, for example a placeholder). ",
            SourceRefusalCodes.McpTypeMismatch, "docx", McpAttachmentParser.DocxContentType, PdfBytes.Length, "letter"),
        new("text pdf", Attachments.Json(("offer.txt", Text, PdfBytes)),
            "The attachment 'offer.txt' is a PDF file, but it was declared as text; use contentType application/pdf. ",
            SourceRefusalCodes.McpTypeMismatch, "txt", Text, PdfBytes.Length, "offer"),
        new("text zip", Attachments.Json(("letter.txt", Text, Attachments.MinimalDocx)),
            "The attachment 'letter.txt' is a binary file (for example a Word document), but it was declared as text; use the matching contentType. ",
            SourceRefusalCodes.McpTypeMismatch, "txt", Text, Attachments.MinimalDocx.Length, "letter"),
        new("no text", Attachments.Text("blank.txt", "  \n\t "), "The attachment 'blank.txt' contains no text. ",
            SourceRefusalCodes.McpEmpty, "txt", Text, 5, "blank"),
        new("short", Attachments.Text("short.txt", "tbd"),
            "The attachment 'short.txt' contains only 3 characters, which looks like a placeholder rather than a file. ",
            SourceRefusalCodes.McpPlaceholder, "txt", Text, 3, "short"),
        new("text twice", Attachments.Text("file.txt", PdfAsBase64),
            "The attachment 'file.txt' contains Base64 text instead of readable text (the file was probably encoded twice). ",
            SourceRefusalCodes.McpBase64Invalid, "txt", Text, PdfAsBase64.Length, "file"),
        new("placeholder", Attachments.Text("vertrag.md", Placeholder, "text/markdown"),
            "The attachment 'vertrag.md' contains only a placeholder or a reference to the file instead of the file content. ",
            SourceRefusalCodes.McpPlaceholder, "md", "text/markdown", Encoding.UTF8.GetByteCount(Placeholder), "vertrag"),
    ];
}
