using System.Diagnostics;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
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

        var wrapped = LineWrapper.Wrap(forged, 1_000, out var didWrap);

        Assert.True(didWrap);
        Assert.Equal([1], PageMarkers.SplitPages(wrapped).Select(p => p.Page));
        Assert.Contains("[ [page 3]]", wrapped, StringComparison.Ordinal);

        var hardCut = LineWrapper.Wrap("[[page 1]]\n" + new string('b', 1_000) + "[[page 9]]", 1_000, out _);
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

    private static PdfSourceExtractor Pdf(SourceOptions options) => new(Options.Create(options), NullLogger<PdfSourceExtractor>.Instance);

    private static DocxSourceExtractor Docx(SourceOptions options) => new(Options.Create(options), NullLogger<DocxSourceExtractor>.Instance);
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
