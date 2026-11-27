using System.Diagnostics;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Geef.Atelier.Tests.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Sources;

/// <summary>Defences inside the extractors and the text pipeline against hostile files, without a separate process.</summary>
public sealed class HostileSourceTests
{
    [Fact]
    public async Task DeeplyNestedWordFileIsRefusedBeforeTheDocumentModelIsBuilt()
    {
        var bomb = HostileDocuments.DeeplyNestedDocx(20_000);
        Assert.True(bomb.Length < 8_192, $"the bomb is only {bomb.Length} bytes");

        var clock = Stopwatch.StartNew();
        var withoutRatioCheck = await Docx(new SourceOptions { MaxCompressionRatio = 1_000_000 }).ExtractAsync(bomb, CancellationToken.None);
        Assert.Equal(SourceQuality.Unreadable, withoutRatioCheck.Quality);
        Assert.Equal(DocxSourceExtractor.NestingReason, withoutRatioCheck.FailureReason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");

        var defaults = await Docx(new SourceOptions()).ExtractAsync(bomb, CancellationToken.None);
        Assert.Equal(SourceQuality.Unreadable, defaults.Quality);
    }

    [Fact]
    public async Task WordFileWithTheDeepestAllowedNestingIsStillRead()
    {
        var result = await Docx(new SourceOptions()).ExtractAsync(HostileDocuments.DeeplyNestedDocx(45, "Nested but fine"), CancellationToken.None);

        Assert.Equal(SourceQuality.Good, result.Quality);
        Assert.Equal("Nested but fine", result.Text);
    }

    [Fact]
    public void CompressionRatioIsCheckedFromSmallEntriesOn()
    {
        Assert.Equal(65_536, DocxSourceExtractor.RatioCheckFromBytes);
        Assert.Equal(67_108_864, new SourceOptions().MaxUncompressedBytes);

        using var archive = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(archive, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("word/document.xml", System.IO.Compression.CompressionLevel.SmallestSize);
            using var writer = entry.Open();
            writer.Write(new byte[200 * 1_024]);
        }

        Assert.False(DocxSourceExtractor.ArchiveIsSafe(archive.ToArray(), new SourceOptions()));
    }

    [Fact]
    public void XmlScanRefusesDtdsAndDeepParts()
    {
        var dtd = HostileDocuments.Docx("<w:p><w:r><w:t>x</w:t></w:r></w:p>")
            .ReplaceDocumentXml(xml => xml.Replace("<w:document", "<!DOCTYPE w:document [<!ENTITY a \"aaaa\">]><w:document", StringComparison.Ordinal));
        Assert.Equal(DocxSourceExtractor.XmlScanResult.Broken, DocxSourceExtractor.ScanXmlParts(dtd, new SourceOptions()));
        Assert.Equal(DocxSourceExtractor.XmlScanResult.TooDeep, DocxSourceExtractor.ScanXmlParts(HostileDocuments.DeeplyNestedDocx(60), new SourceOptions { MaxXmlDepth = 50 }));
        Assert.Equal(DocxSourceExtractor.XmlScanResult.Fine, DocxSourceExtractor.ScanXmlParts(SyntheticDocuments.Docx(["Hello"]), new SourceOptions()));
    }

    [Fact]
    public async Task PdfWhoseStreamsUnpackBeyondTheLimitIsRefusedBeforeOpening()
    {
        var bomb = HostileDocuments.FlateBombPdf(8 * 1_048_576);
        Assert.True(bomb.Length < 64 * 1_024, $"the bomb is only {bomb.Length} bytes");
        Assert.True(bomb.ExceedsInflatedLimit(1_048_576));
        Assert.False(bomb.ExceedsInflatedLimit(16 * 1_048_576));
        Assert.False(SyntheticDocuments.Pdf(3, p => $"Page {p} text.").ExceedsInflatedLimit(1_048_576));

        var clock = Stopwatch.StartNew();
        var result = await Pdf(new SourceOptions { MaxPdfInflatedBytes = 1_048_576 }).ExtractAsync(bomb, CancellationToken.None);
        Assert.Equal(SourceQuality.Unreadable, result.Quality);
        Assert.Equal(PdfSourceExtractor.InflatedReason, result.FailureReason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"took {clock.Elapsed}");
    }

    [Fact]
    public void WrappingALongLineNeverCreatesAPageMarker()
    {
        var forged = "[[page 1]]\n" + new string('a', 995) + " [[page 3]]\nmore text";

        var wrapped = Wrap(forged, 1_000, out var didWrap);

        Assert.True(didWrap);
        Assert.Equal([1], PageMarkers.SplitPages(wrapped).Select(p => p.Page));
        Assert.Contains("[ [page 3]]", wrapped, StringComparison.Ordinal);

        var hardCut = Wrap("[[page 1]]\n" + new string('b', 1_000) + "[[page 9]]", 1_000, out _);
        Assert.Equal([1], PageMarkers.SplitPages(hardCut).Select(p => p.Page));
    }

    [Theory]
    [InlineData("[[page 2]]")]
    [InlineData("  [[page 2]]  ")]
    [InlineData("[[Page 1234567]]")]
    [InlineData("[[page 2]]\r")]
    public void MarkerLookAlikesInTextFilesAreNeutralised(string line)
    {
        var text = $"Intro\n{line}\nOutro".DefusePageMarkerLines();

        Assert.Equal([1], PageMarkers.SplitPages(text).Select(p => p.Page));
        Assert.DoesNotContain("[[", text, StringComparison.Ordinal);
        Assert.Equal("Intro", text.Split('\n')[0]);
    }

    [Fact]
    public async Task AWordTextBombOfAFewKilobytesIsRefusedWhileTheArchiveIsScanned()
    {
        var bomb = HostileDocuments.TextBombDocx(3_200_000);
        Assert.True(bomb.Length < 16 * 1_024, $"the bomb is only {bomb.Length} bytes");
        var withoutRatioCheck = new SourceOptions { MaxCompressionRatio = 1_000_000 };

        var clock = Stopwatch.StartNew();
        Assert.Equal(DocxSourceExtractor.XmlScanResult.TooMuchText, DocxSourceExtractor.ScanXmlParts(bomb, withoutRatioCheck));
        var result = await Docx(withoutRatioCheck).ExtractAsync(bomb, CancellationToken.None);

        Assert.Equal(SourceQuality.Unreadable, result.Quality);
        Assert.Equal(SourceExtraction.TooMuchTextReason, result.FailureReason);
        Assert.Equal("This file contains far more text than we can use. Please upload a shorter version.", result.FailureReason);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"took {clock.Elapsed}");
        Assert.Equal(DocxSourceExtractor.XmlScanResult.Fine, DocxSourceExtractor.ScanXmlParts(HostileDocuments.TextBombDocx(2_900_000), withoutRatioCheck));
        Assert.Equal(SourceQuality.Unreadable, (await Docx(new SourceOptions()).ExtractAsync(bomb, CancellationToken.None)).Quality);
    }

    [Fact]
    public async Task WordTextThatGrowsBeyondTheLimitOnlyWhileWalkingIsRefusedToo()
    {
        var runs = string.Concat(Enumerable.Repeat("<w:r><w:t>a</w:t><w:tab/></w:r>", 600));
        var docx = HostileDocuments.Docx($"<w:p>{runs}</w:p>");
        var options = new SourceOptions { MaxExtractedChars = 1_000 };

        Assert.Equal(DocxSourceExtractor.XmlScanResult.Fine, DocxSourceExtractor.ScanXmlParts(docx, options));
        var result = await Docx(options).ExtractAsync(docx, CancellationToken.None);

        Assert.Equal(SourceExtraction.TooMuchTextReason, result.FailureReason);
        Assert.Equal(SourceQuality.Good, (await Docx(new SourceOptions { MaxExtractedChars = 1_300 }).ExtractAsync(docx, CancellationToken.None)).Quality);
    }

    [Fact]
    public async Task PdfReadingStopsAtTheTextLimitWithThePageCount()
    {
        var pdf = SyntheticDocuments.Pdf(4, p => string.Concat(Enumerable.Repeat($"Page {p} has a lot of words in it. ", 20)));

        var refused = await Pdf(new SourceOptions { MaxExtractedChars = 1_000 }).ExtractAsync(pdf, CancellationToken.None);
        var read = await Pdf(new SourceOptions()).ExtractAsync(pdf, CancellationToken.None);

        Assert.Equal(SourceExtraction.TooMuchTextReason, refused.FailureReason);
        Assert.Equal(4, refused.PageCount);
        Assert.Equal(SourceQuality.Good, read.Quality);
        Assert.True(read.Text!.Length > 1_000);
    }

    [Fact]
    public void TextFilesAreCountedInCharactersBeforeTheyAreDecoded()
    {
        Assert.Equal(SourceExtraction.TooMuchTextReason, PlainTextSourceExtractor.Extract(System.Text.Encoding.UTF8.GetBytes(new string('x', 11)), maxChars: 10).FailureReason);
        Assert.Equal(SourceQuality.Good, PlainTextSourceExtractor.Extract(System.Text.Encoding.UTF8.GetBytes(new string('x', 10)), maxChars: 10).Quality);
        Assert.Equal(SourceQuality.Good, PlainTextSourceExtractor.Extract(System.Text.Encoding.UTF8.GetBytes("Grüße äöü ß!"), maxChars: 12).Quality);

        byte[] utf16 = [0xFF, 0xFE, .. System.Text.Encoding.Unicode.GetBytes("Hello")];
        Assert.Equal("Hello", PlainTextSourceExtractor.Extract(utf16, maxChars: 5).Text);
        Assert.Equal(SourceExtraction.TooMuchTextReason, PlainTextSourceExtractor.Extract(utf16, maxChars: 4).FailureReason);
    }

    [Theory]
    [InlineData("intro\r[[page 7]]\rsecret")]
    [InlineData("intro\r\n[[page 7]]\r\nsecret")]
    [InlineData("intro\n [[page 7]] \nsecret")]
    public void ALoneCarriageReturnNeverStartsAPage(string text)
    {
        var normalized = text.NormalizeForStorage(defuseMarkerLines: true, 1_000);

        Assert.Equal([1], PageMarkers.SplitPages(normalized.Text).Select(p => p.Page));
        Assert.DoesNotContain('\r', normalized.Text);
        Assert.Contains("[ [page 7]]", normalized.Text, StringComparison.Ordinal);
        Assert.Equal(3, normalized.Text.Split('\n').Length);
    }

    [Fact]
    public void ArchivesWithTooManyEntriesAreRefusedFromTheirEndRecordBeforeTheyAreListed()
    {
        using var many = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(many, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            zip.CreateEntry("word/document.xml");
            for (var i = 0; i < 20; i++)
                zip.CreateEntry($"word/media/{i}.bin");
        }

        var bytes = many.ToArray();
        Assert.Equal(DocxSourceExtractor.ArchiveReason, SourceKindDetector.Detect(bytes, "a.docx", maxArchiveEntries: 10).Reason);
        Assert.Equal(SourceKind.Docx, SourceKindDetector.Detect(bytes, "a.docx", maxArchiveEntries: 21).Kind);

        var end = bytes.AsSpan().LastIndexOf([(byte)0x50, (byte)0x4B, (byte)0x05, (byte)0x06]);
        var zip64 = bytes.ToArray();
        zip64[end + 10] = 0xFF;
        zip64[end + 11] = 0xFF;
        Assert.Equal(DocxSourceExtractor.ArchiveReason, SourceKindDetector.Detect(zip64, "a.docx").Reason);
        Assert.Equal(SourceKind.Docx, SourceKindDetector.Detect(SyntheticDocuments.Docx(["Hello"]), "a.docx").Kind);
    }

    private static PdfSourceExtractor Pdf(SourceOptions options) => new(Options.Create(options), NullLogger<PdfSourceExtractor>.Instance);

    private static DocxSourceExtractor Docx(SourceOptions options) => new(Options.Create(options), NullLogger<DocxSourceExtractor>.Instance);

    private static string Wrap(string text, int maxLineChars, out bool wrapped)
    {
        var result = text.NormalizeForStorage(defuseMarkerLines: false, maxLineChars);
        wrapped = result.Wrapped;
        return result.Text;
    }
}

internal static class DocxBytesExtensions
{
    public static byte[] ReplaceDocumentXml(this byte[] docx, Func<string, string> change)
    {
        using var input = new MemoryStream(docx);
        using var output = new MemoryStream();
        using (var source = new System.IO.Compression.ZipArchive(input, System.IO.Compression.ZipArchiveMode.Read))
        using (var target = new System.IO.Compression.ZipArchive(output, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var reader = new StreamReader(entry.Open());
                var content = reader.ReadToEnd();
                using var writer = new StreamWriter(target.CreateEntry(entry.FullName).Open());
                writer.Write(entry.FullName == "word/document.xml" ? change(content) : content);
            }
        }

        return output.ToArray();
    }
}
