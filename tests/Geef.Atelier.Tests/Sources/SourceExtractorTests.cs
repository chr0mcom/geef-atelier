using System.Text;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Geef.Atelier.Tests.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Sources;

public sealed class SourceExtractorTests
{
    private const string FirstMarker = "MARKER-ALPHA-0001";
    private const string LastMarker = "MARKER-OMEGA-0080";

    [Fact]
    public async Task PdfWithEightyPagesKeepsEveryPageMarkerBothMarkersAndShortLines()
    {
        var pdf = SyntheticDocuments.Pdf(80, page => page switch
        {
            1 => $"Opening page.\n{FirstMarker} stands on the first page.",
            2 => SyntheticDocuments.TwoColumns + "Left column first sentence. Left column second sentence.||Right column first sentence. Right column second sentence.",
            80 => $"Closing page.\n{LastMarker} stands on the last page.",
            _ => $"Page {page} talks about topic {page}. " + string.Concat(Enumerable.Repeat("Some ordinary words for this page. ", 12)),
        });

        var result = await Pdf().ExtractAsync(pdf, CancellationToken.None);

        Assert.Null(result.FailureReason);
        Assert.Equal(SourceQuality.Good, result.Quality);
        Assert.Equal(80, result.PageCount);
        var pages = PageMarkers.SplitPages(result.Text!);
        Assert.Equal(Enumerable.Range(1, 80), pages.Select(p => p.Page));
        Assert.Contains(FirstMarker, pages[0].Text, StringComparison.Ordinal);
        Assert.Contains(LastMarker, pages[79].Text, StringComparison.Ordinal);

        var two = pages[1].Text;
        Assert.True(two.IndexOf("Left column second", StringComparison.Ordinal) < two.IndexOf("Right column first", StringComparison.Ordinal),
            "the left column is read before the right column");

        var wrapped = LineWrapper.Wrap(result.Text!, 1_000, out _);
        Assert.All(wrapped.Split('\n'), line => Assert.True(line.Length <= 1_000));
    }

    [Fact]
    public async Task PdfWithAnEmptyPageIsPartialAndNamesThePage()
    {
        var pdf = SyntheticDocuments.Pdf(3, page => page == 2 ? string.Empty : string.Concat(Enumerable.Repeat($"Page {page} has plenty of readable words. ", 10)));

        var result = await Pdf().ExtractAsync(pdf, CancellationToken.None);

        Assert.Equal(SourceQuality.Partial, result.Quality);
        Assert.Equal([2], result.EmptyPages);
        var warning = Assert.Single(result.Warnings, w => w.Code == SourceWarningCodes.PagesWithoutText);
        Assert.Equal([2], warning.Pages);
        Assert.Equal("Page 2 has no readable text.", warning.Message);
        Assert.Contains("[[page 2]]\n[[page 3]]", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PdfWithLittleTextIsPoorAndWithoutTextUnreadable()
    {
        var thin = await Pdf().ExtractAsync(SyntheticDocuments.Pdf(4, p => $"Short page {p} text here."), CancellationToken.None);
        Assert.Equal(SourceQuality.Poor, thin.Quality);
        Assert.Contains(thin.Warnings, w => w.Code == SourceWarningCodes.LikelyScanned);

        var empty = await Pdf().ExtractAsync(SyntheticDocuments.Pdf(3, _ => string.Empty), CancellationToken.None);
        Assert.Equal(SourceQuality.Unreadable, empty.Quality);
        Assert.Equal(PdfSourceExtractor.NoTextReason, empty.FailureReason);
    }

    [Fact]
    public async Task EncryptedOrBrokenPdfsAreUnreadable()
    {
        var encrypted = await Pdf().ExtractAsync(SyntheticDocuments.EncryptedPdf(), CancellationToken.None);
        Assert.Equal(SourceQuality.Unreadable, encrypted.Quality);
        Assert.Equal(PdfSourceExtractor.EncryptedReason, encrypted.FailureReason);
        Assert.Contains(encrypted.Warnings, w => w.Code == SourceWarningCodes.Encrypted);

        var broken = await Pdf().ExtractAsync("%PDF-1.7\nthis is not really a pdf"u8.ToArray(), CancellationToken.None);
        Assert.Equal(SourceQuality.Unreadable, broken.Quality);
    }

    [Fact]
    public async Task PdfWithTooManyPagesIsRefusedBeforeReading()
    {
        var result = await Pdf(new SourceOptions { MaxPages = 5 }).ExtractAsync(SyntheticDocuments.Pdf(6, p => $"Page {p}"), CancellationToken.None);
        Assert.Equal(SourceQuality.Unreadable, result.Quality);
        Assert.Equal(6, result.PageCount);
        Assert.Contains(result.Warnings, w => w.Code == SourceWarningCodes.TooManyPages);
        Assert.Equal("This PDF has 6 pages; at most 5 are possible. Please split it or remove pages.", result.FailureReason);
    }

    [Fact]
    public async Task PdfFallsBackToPlainOrderWhenLayoutIsTooSlow()
    {
        var extractor = Pdf(new SourceOptions { PageLayoutTimeout = TimeSpan.Zero });
        var pdf = SyntheticDocuments.Pdf(5, p => string.Concat(Enumerable.Repeat($"Words on page {p}. ", 20)));

        var result = await extractor.ExtractAsync(pdf, CancellationToken.None);

        Assert.Equal(SourceQuality.Good, result.Quality);
        Assert.Contains("Words on page 5.", result.Text, StringComparison.Ordinal);
        Assert.Equal(5, extractor.LastStats!.PlainPages);
    }

    [Fact]
    public async Task DocxBecomesMarkdownWithoutHeadersAndWarnsAboutPictures()
    {
        var docx = SyntheticDocuments.Docx(
        [
            "# Offer for the garden project",
            "Thank you for your request.",
            "## Prices",
            "- Planting",
            "- Lawn care",
            "| Item | Price |",
            "| Hedge | 300 EUR |",
            "[picture]",
            "Kind regards",
        ]);

        var result = await Docx().ExtractAsync(docx, CancellationToken.None);

        Assert.Equal(SourceQuality.Good, result.Quality);
        Assert.Equal(
            "# Offer for the garden project\n\nThank you for your request.\n\n## Prices\n\n- Planting\n- Lawn care\n\n| Item | Price |\n| --- | --- |\n| Hedge | 300 EUR |\n\nKind regards",
            result.Text);
        Assert.DoesNotContain("HEADER-ONLY-TEXT", result.Text, StringComparison.Ordinal);
        Assert.Equal("1 picture was skipped; only the text is used.", Assert.Single(result.Warnings).Message);
    }

    [Fact]
    public async Task DocxWithoutTextOrBrokenIsUnreadable()
    {
        Assert.Equal(DocxSourceExtractor.NoTextReason, (await Docx().ExtractAsync(SyntheticDocuments.Docx([]), CancellationToken.None)).FailureReason);
        Assert.Equal(SourceQuality.Unreadable, (await Docx().ExtractAsync("PK\u0003\u0004 broken"u8.ToArray(), CancellationToken.None)).Quality);
    }

    [Fact]
    public void ZipBombLimitsRejectHugeOrHighlyCompressedArchives()
    {
        var docx = SyntheticDocuments.Docx(["Hello"]);
        Assert.True(DocxSourceExtractor.ArchiveIsSafe(docx, new SourceOptions()));
        Assert.False(DocxSourceExtractor.ArchiveIsSafe(docx, new SourceOptions { MaxArchiveEntries = 2 }));
        Assert.False(DocxSourceExtractor.ArchiveIsSafe(docx, new SourceOptions { MaxUncompressedBytes = 100 }));

        using var bomb = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(bomb, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml", System.IO.Compression.CompressionLevel.SmallestSize);
            using var writer = entry.Open();
            writer.Write(new byte[8 * 1_048_576]);
        }

        Assert.False(DocxSourceExtractor.ArchiveIsSafe(bomb.ToArray(), new SourceOptions()));
    }

    [Fact]
    public void PlainTextDetectsUtf8Utf16AndWindows1252()
    {
        const string text = "Grüße aus Köln – 5 €";
        Assert.Equal(text, Plain(Encoding.UTF8.GetBytes(text), out var utf8Warnings));
        Assert.Empty(utf8Warnings);

        Assert.Equal(text, Plain([.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)], out _));
        Assert.Equal(text, Plain([.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)], out _));
        Assert.Equal(text, Plain([.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)], out _));
        Assert.Equal(text, Plain(Encoding.Unicode.GetBytes(text), out var guessed16));
        Assert.Contains(guessed16, w => w.Code == SourceWarningCodes.EncodingGuessed);

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Assert.Equal(text, Plain(Encoding.GetEncoding(1252).GetBytes(text), out var guessed));
        Assert.Contains(guessed, w => w.Code == SourceWarningCodes.EncodingGuessed);
    }

    [Fact]
    public void PlainTextWarnsAboutReplacementCharactersAndRefusesEmptyFiles()
    {
        var result = PlainTextSourceExtractor.Extract(Encoding.UTF8.GetBytes("Broken � char"));
        Assert.Contains(result.Warnings, w => w.Code == SourceWarningCodes.ReplacementCharacters);
        Assert.Equal(SourceQuality.Unreadable, PlainTextSourceExtractor.Extract("   \r\n "u8).Quality);
    }

    [Theory]
    [InlineData("%PDF-1.4 rest", "a.txt", SourceKind.Pdf)]
    [InlineData("# Title\nText", "notes.md", SourceKind.Markdown)]
    [InlineData("# Title\nText", "notes.txt", SourceKind.Text)]
    [InlineData("Plain", "renamed.pdf", SourceKind.Text)]
    public void DetectorTrustsContentNotNames(string content, string name, SourceKind kind) =>
        Assert.Equal(kind, SourceKindDetector.Detect(Encoding.UTF8.GetBytes(content), name).Kind);

    [Fact]
    public void DetectorRefusesBinariesOldWordAndOtherArchives()
    {
        byte[] exe = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00];
        var asPdf = SourceKindDetector.Detect(exe, "invoice.pdf");
        Assert.Null(asPdf.Kind);
        Assert.Equal(SourceKindDetector.UnsupportedReason, asPdf.Reason);
        Assert.Equal(SourceKindDetector.OldWordReason, SourceKindDetector.Detect([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1], "x.doc").Reason);

        using var zip = new MemoryStream();
        using (var archive = new System.IO.Compression.ZipArchive(zip, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            archive.CreateEntry("xl/workbook.xml");
        Assert.Null(SourceKindDetector.Detect(zip.ToArray(), "sheet.docx").Kind);
        Assert.Equal(SourceKind.Docx, SourceKindDetector.Detect(SyntheticDocuments.Docx(["Hi"]), "file.bin").Kind);
    }

    [Fact]
    public void LineWrapperBreaksLongLinesAtSpaces()
    {
        var longLine = string.Join(' ', Enumerable.Repeat("word", 600));
        var wrapped = LineWrapper.Wrap("short\n" + longLine, 1_000, out var didWrap);
        Assert.True(didWrap);
        Assert.All(wrapped.Split('\n'), l => Assert.True(l.Length <= 1_000));
        Assert.Equal(longLine.Replace(" ", string.Empty, StringComparison.Ordinal), string.Concat(wrapped.Split('\n').Skip(1)).Replace(" ", string.Empty, StringComparison.Ordinal));

        var noSpaces = new string('x', 2_500);
        Assert.Equal([1_000, 1_000, 500], LineWrapper.Wrap(noSpaces, 1_000, out _).Split('\n').Select(l => l.Length));
        Assert.Equal("a\nb", LineWrapper.Wrap("a\r\nb", 1_000, out var untouched));
        Assert.False(untouched);
    }

    private static string Plain(byte[] bytes, out List<SourceWarning> warnings)
    {
        var result = PlainTextSourceExtractor.Extract(bytes);
        warnings = result.Warnings.ToList();
        return result.Text!;
    }

    private static PdfSourceExtractor Pdf(SourceOptions? options = null) =>
        new(Options.Create(options ?? new SourceOptions()), NullLogger<PdfSourceExtractor>.Instance);

    private static DocxSourceExtractor Docx() =>
        new(Options.Create(new SourceOptions()), NullLogger<DocxSourceExtractor>.Instance);
}
