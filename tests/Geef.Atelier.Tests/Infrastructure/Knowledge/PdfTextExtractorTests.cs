using Geef.Atelier.Infrastructure.Knowledge;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Knowledge;

public sealed class PdfTextExtractorTests
{
    private static PdfTextExtractor CreateExtractor() =>
        new(NullLogger<PdfTextExtractor>.Instance);

    [Fact]
    public void ExtractText_WithNonPdfBytes_ReturnsFailed()
    {
        var extractor = CreateExtractor();
        var result = extractor.ExtractText("this is not a pdf"u8.ToArray());

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
        Assert.Null(result.Text);
    }

    [Fact]
    public void ExtractText_WithEmptyBytes_ReturnsFailed()
    {
        var extractor = CreateExtractor();
        var result = extractor.ExtractText([]);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Text);
    }

    [Fact]
    public void ExtractText_WithMinimalTextPdf_ReturnsText()
    {
        var extractor = CreateExtractor();
        var pdfBytes = BuildMinimalTextPdf("Hello from PDF");

        var result = extractor.ExtractText(pdfBytes);

        Assert.True(result.IsSuccess, result.ErrorMessage ?? "expected success");
        Assert.NotNull(result.Text);
        Assert.Contains("Hello", result.Text);
        Assert.True(result.PageCount > 0);
    }

    [Fact]
    public void PdfExtractionResult_SuccessFactory_SetsCorrectFields()
    {
        var result = PdfExtractionResult.Success("sample text", 3);

        Assert.True(result.IsSuccess);
        Assert.Equal("sample text", result.Text);
        Assert.Equal(3, result.PageCount);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void PdfExtractionResult_FailedFactory_SetsCorrectFields()
    {
        var result = PdfExtractionResult.Failed("something went wrong");

        Assert.False(result.IsSuccess);
        Assert.Equal("something went wrong", result.ErrorMessage);
        Assert.Null(result.Text);
        Assert.Null(result.PageCount);
    }

    /// <summary>
    /// Builds a syntactically valid minimal PDF with a single page containing text.
    /// Uses raw PDF syntax to avoid a PdfPig dependency in tests.
    /// </summary>
    private static byte[] BuildMinimalTextPdf(string text)
    {
        // Minimal valid PDF 1.4 with one page and one text object
        var pdf = $"""
%PDF-1.4
1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj
2 0 obj<</Type/Pages/Kids[3 0 R]/Count 1>>endobj
3 0 obj<</Type/Page/MediaBox[0 0 612 792]/Contents 4 0 R/Resources<</Font<</F1<</Type/Font/Subtype/Type1/BaseFont/Helvetica>>>>>>>/Parent 2 0 R>>endobj
4 0 obj<</Length {24 + text.Length}>>
stream
BT /F1 12 Tf 100 700 Td ({text}) Tj ET
endstream
endobj
xref
0 5
0000000000 65535 f
0000000009 00000 n
0000000058 00000 n
0000000115 00000 n
0000000266 00000 n
trailer<</Size 5/Root 1 0 R>>
startxref
{370 + text.Length}
%%EOF
""";
        return System.Text.Encoding.Latin1.GetBytes(pdf);
    }
}
