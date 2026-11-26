using System.Text;
using Geef.Atelier.Mcp.Tools;
using ModelContextProtocol;

namespace Geef.Atelier.Tests.Mcp.Orders;

public sealed class McpAttachmentParserTests
{
    private const string WebAppHint = "Atelier web app";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("[]")]
    public void Nothing_attached_gives_an_empty_list(string? json) =>
        Assert.Empty(McpAttachmentParser.Parse(json));

    [Fact]
    public void Pdf_docx_markdown_and_text_are_accepted_with_normalised_types()
    {
        var markdown = Encoding.UTF8.GetBytes("# Notes\n\nThe customer wants delivery in March.");
        var text = Encoding.UTF8.GetBytes("Delivery address: Example Street 1, Sampletown.");
        var json = Attachments.Json(
            ("offer.pdf", "application/pdf", Attachments.MinimalPdf),
            ("letter.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", Attachments.MinimalDocx),
            ("notes.md", "text/x-markdown", markdown),
            ("address.txt", "text/plain; charset=utf-8", text));

        var parsed = McpAttachmentParser.Parse(json);

        Assert.Equal(4, parsed.Count);
        Assert.Equal(("offer.pdf", McpAttachmentParser.PdfContentType), (parsed[0].FileName, parsed[0].ContentType));
        Assert.Equal(Attachments.MinimalPdf, parsed[0].Content);
        Assert.Equal(McpAttachmentParser.DocxContentType, parsed[1].ContentType);
        Assert.Equal(McpAttachmentParser.MarkdownContentType, parsed[2].ContentType);
        Assert.Equal(McpAttachmentParser.PlainTextContentType, parsed[3].ContentType);
        Assert.Equal(text, parsed[3].Content);
    }

    [Fact]
    public void Base64_with_line_breaks_is_accepted()
    {
        var b64 = Convert.ToBase64String(Attachments.MinimalPdf, Base64FormattingOptions.InsertLineBreaks);
        var json = $$"""[{"filename":"a.pdf","contentType":"application/pdf","contentBase64":"{{b64.Replace("\r\n", "\\r\\n")}}"}]""";

        Assert.Equal(Attachments.MinimalPdf, McpAttachmentParser.Parse(json)[0].Content);
    }

    [Theory]
    [InlineData("not valid json{{")]
    [InlineData("""{"filename":"a.txt"}""")]
    [InlineData("""[{"filename":"a.txt","contentType":"text/plain"}]""")]
    public void Malformed_json_is_rejected_with_the_expected_format(string json)
    {
        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains("filename, contentType and contentBase64", ex.Message);
    }

    [Theory]
    [InlineData("application/msword")]
    [InlineData("application/octet-stream")]
    [InlineData("image/png")]
    public void Unsupported_content_types_are_rejected_by_name(string contentType)
    {
        var json = Attachments.Json(("doc.bin", contentType, "binary data"u8.ToArray()));

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains(contentType, ex.Message);
        Assert.Contains("doc.bin", ex.Message);
    }

    [Theory]
    [InlineData("!!!not-base64!!!")]
    [InlineData("JVBERi0xLjQKJ...")]
    [InlineData("<base64 of the PDF>")]
    public void Invalid_base64_is_rejected_with_the_right_way(string content)
    {
        var json = $$"""[{"filename":"doc.pdf","contentType":"application/pdf","contentBase64":"{{content}}"}]""";

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains("doc.pdf", ex.Message);
        Assert.Contains("not valid Base64", ex.Message);
        Assert.Contains(WebAppHint, ex.Message);
    }

    [Fact]
    public void Empty_content_is_rejected()
    {
        var json = """[{"filename":"doc.txt","contentType":"text/plain","contentBase64":""}]""";

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains("has no content", ex.Message);
    }

    [Theory]
    [InlineData("Siehe angehängte Vollversion im Briefing-Kontext.")]
    [InlineData("See attached full version of the contract.")]
    [InlineData("[Datei: Angebot_2026.pdf]")]
    [InlineData("Content omitted for brevity, the full document is 80 pages.")]
    [InlineData("Platzhalter für das Protokoll der Sitzung")]
    [InlineData("Refer to the attachment in the previous message.")]
    [InlineData("Inhalt gekürzt – vollständige Fassung liegt vor.")]
    public void Placeholder_text_is_rejected_with_the_right_way(string text)
    {
        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(Attachments.Text("full.md", text, "text/markdown")));

        Assert.Contains("placeholder", ex.Message);
        Assert.Contains("contentBase64", ex.Message);
        Assert.Contains(WebAppHint, ex.Message);
    }

    [Theory]
    [InlineData("...")]
    [InlineData("tbd")]
    [InlineData("  \n\t ")]
    public void Too_short_text_is_rejected(string text)
    {
        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(Attachments.Text("short.txt", text)));
        Assert.Contains("short.txt", ex.Message);
    }

    [Fact]
    public void Placeholder_words_inside_a_long_real_text_are_accepted()
    {
        var text = "Meeting notes. " + string.Join(" ", Enumerable.Repeat("The team agreed on the schedule and the budget for the next quarter.", 6))
            + " Weitere Details siehe Anhang des Protokolls.";

        var parsed = McpAttachmentParser.Parse(Attachments.Text("notes.txt", text));

        Assert.Single(parsed);
    }

    [Fact]
    public void A_short_real_text_is_accepted()
    {
        var parsed = McpAttachmentParser.Parse(Attachments.Text("address.txt", "Max Example, Sample Road 5, 12345 Town"));
        Assert.Single(parsed);
    }

    [Fact]
    public void Text_that_is_itself_base64_is_rejected_as_encoded_twice()
    {
        var once = Convert.ToBase64String(Attachments.MinimalPdf);

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(Attachments.Text("file.txt", once)));
        Assert.Contains("encoded twice", ex.Message);
    }

    [Fact]
    public void Pdf_whose_content_is_a_placeholder_text_is_rejected()
    {
        var json = Attachments.Json(("offer.pdf", "application/pdf", "Siehe angehängte Vollversion im Briefing-Kontext"u8.ToArray()));

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains("not a PDF", ex.Message);
        Assert.Contains("placeholder", ex.Message);
    }

    [Fact]
    public void Pdf_encoded_twice_is_rejected_as_encoded_twice()
    {
        var twice = Encoding.ASCII.GetBytes(Convert.ToBase64String(Attachments.MinimalPdf));
        var json = Attachments.Json(("offer.pdf", "application/pdf", twice));

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains("encoded twice", ex.Message);
    }

    [Fact]
    public void Docx_with_pdf_bytes_is_rejected()
    {
        var json = Attachments.Json(("letter.docx", McpAttachmentParser.DocxContentType, Attachments.MinimalPdf));

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains("not a .docx file", ex.Message);
    }

    [Fact]
    public void Text_with_pdf_bytes_is_rejected_with_the_matching_type()
    {
        var json = Attachments.Json(("offer.txt", "text/plain", Attachments.MinimalPdf));

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains("application/pdf", ex.Message);
    }

    [Fact]
    public void Text_with_zip_bytes_is_rejected()
    {
        var json = Attachments.Json(("letter.txt", "text/plain", Attachments.MinimalDocx));

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains("binary file", ex.Message);
    }

    [Fact]
    public void Attachment_without_filename_is_rejected()
    {
        var json = """[{"filename":" ","contentType":"text/plain","contentBase64":"SGVsbG8gd29ybGQgaGVsbG8="}]""";

        var ex = Assert.ThrowsAny<McpException>(() => McpAttachmentParser.Parse(json));
        Assert.Contains("filename", ex.Message);
    }
}
