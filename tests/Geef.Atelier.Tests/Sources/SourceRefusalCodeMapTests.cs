using System.Reflection;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;

namespace Geef.Atelier.Tests.Sources;

/// <summary>Codes of refusals: every sentence of the detection and the extraction has its own code; extension and media type are cleaned.</summary>
public sealed class SourceRefusalCodeMapTests
{
    public static TheoryData<string, string> ReasonConstants()
    {
        var data = new TheoryData<string, string>();
        foreach (var field in ReasonFields())
            data.Add($"{field.DeclaringType!.Name}.{field.Name}", (string)field.GetRawConstantValue()!);
        return data;
    }

    [Theory]
    [MemberData(nameof(ReasonConstants))]
    public void Every_sentence_constant_of_the_intake_has_a_code_other_than_other(string constant, string sentence)
    {
        Assert.True(SourceRefusalCodeMap.FromExtractionReason(sentence) != SourceRefusalCodes.Other, constant);
    }

    [Fact]
    public void The_scan_finds_the_sentences_of_detection_extraction_limits_and_intake()
    {
        var names = ReasonFields().Select(f => $"{f.DeclaringType!.Name}.{f.Name}").ToHashSet();

        Assert.Superset(new HashSet<string>
        {
            "SourceKindDetector.UnsupportedReason", "SourceKindDetector.OldWordReason", "SourceIntakeLimiter.BusyReason",
            "DocxSourceExtractor.BrokenReason", "DocxSourceExtractor.ArchiveReason", "DocxSourceExtractor.NoTextReason", "DocxSourceExtractor.NestingReason",
            "PdfSourceExtractor.NoTextReason", "PdfSourceExtractor.EncryptedReason", "PdfSourceExtractor.BrokenReason", "PdfSourceExtractor.TooSlowReason",
            "PdfSourceExtractor.InflatedReason", "IsolatedExtractionProcess.TooComplexReason", "IsolatedExtractionProcess.FailedReason",
            "IsolatedExtractionProcess.UnavailableReason", "IsolatedExtractionProcess.TooSlowReason", "PlainTextSourceExtractor.EmptyReason",
            "SourceExtraction.TooMuchTextReason", "SourceIntakeService.SaveFailedReason", "SourceIntakeService.InterruptedReason",
            "SourceIntakeService.EmptyReason", "SourceIntakeService.NoReadableTextReason",
        }, names);
    }

    [Theory]
    [InlineData(SourceKindDetector.UnsupportedReason, SourceRefusalCodes.UnsupportedType)]
    [InlineData(SourceKindDetector.OldWordReason, SourceRefusalCodes.OldWord)]
    [InlineData(DocxSourceExtractor.ArchiveReason, SourceRefusalCodes.ArchiveRefused)]
    [InlineData(DocxSourceExtractor.NestingReason, SourceRefusalCodes.TooComplex)]
    [InlineData(PdfSourceExtractor.InflatedReason, SourceRefusalCodes.TooComplex)]
    [InlineData(IsolatedExtractionProcess.TooComplexReason, SourceRefusalCodes.TooComplex)]
    [InlineData(DocxSourceExtractor.BrokenReason, SourceRefusalCodes.Unreadable)]
    [InlineData(PdfSourceExtractor.BrokenReason, SourceRefusalCodes.Unreadable)]
    [InlineData(IsolatedExtractionProcess.FailedReason, SourceRefusalCodes.Unreadable)]
    [InlineData(DocxSourceExtractor.NoTextReason, SourceRefusalCodes.Empty)]
    [InlineData(PlainTextSourceExtractor.EmptyReason, SourceRefusalCodes.Empty)]
    [InlineData(SourceIntakeService.EmptyReason, SourceRefusalCodes.Empty)]
    [InlineData(SourceIntakeService.NoReadableTextReason, SourceRefusalCodes.Empty)]
    [InlineData(PdfSourceExtractor.NoTextReason, SourceRefusalCodes.PdfNoText)]
    [InlineData(PdfSourceExtractor.EncryptedReason, SourceRefusalCodes.PdfEncrypted)]
    [InlineData(PdfSourceExtractor.TooSlowReason, SourceRefusalCodes.Timeout)]
    [InlineData(IsolatedExtractionProcess.TooSlowReason, SourceRefusalCodes.Timeout)]
    [InlineData(SourceIntakeLimiter.BusyReason, SourceRefusalCodes.Busy)]
    [InlineData(IsolatedExtractionProcess.UnavailableReason, SourceRefusalCodes.Unavailable)]
    [InlineData(SourceExtraction.TooMuchTextReason, SourceRefusalCodes.TooMuchText)]
    [InlineData(SourceIntakeService.SaveFailedReason, SourceRefusalCodes.SaveFailed)]
    [InlineData(SourceIntakeService.InterruptedReason, SourceRefusalCodes.Interrupted)]
    public void Each_sentence_maps_to_its_code(string sentence, string code)
    {
        Assert.Equal(code, SourceRefusalCodeMap.FromExtractionReason(sentence));
    }

    [Fact]
    public void The_sentence_of_the_detector_for_an_empty_file_is_the_one_of_the_intake()
    {
        var detected = SourceKindDetector.DetectFile(Path.Combine(Path.GetTempPath(), "missing"), [], "empty.txt", 10);

        Assert.Equal(SourceIntakeService.EmptyReason, detected.Reason);
    }

    [Fact]
    public void Too_many_pages_is_told_apart_by_its_warning_and_unknown_sentences_are_other()
    {
        var warning = new SourceWarning(SourceWarningCodes.TooManyPages, "The PDF has more than 400 pages.");

        Assert.Equal(SourceRefusalCodes.TooManyPages, SourceRefusalCodeMap.FromExtractionReason("This PDF has 900 pages; at most 400 are possible.", [warning]));
        Assert.Equal(SourceRefusalCodes.Other, SourceRefusalCodeMap.FromExtractionReason("Something nobody wrote."));
        Assert.Equal(SourceRefusalCodes.Other, SourceRefusalCodeMap.FromExtractionReason(null));
    }

    [Theory]
    [InlineData("Report.PDF", "pdf")]
    [InlineData("notes.final.md", "md")]
    [InlineData("data.xlsx", "xlsx")]
    [InlineData("mail.eml", "eml")]
    [InlineData("archive.tar.gz", "gz")]
    [InlineData("a.12345678", "12345678")]
    [InlineData("a.123456789", null)]
    [InlineData("no extension", null)]
    [InlineData("ends with dot.", null)]
    [InlineData("weird.p df", null)]
    [InlineData("../../etc/passwd", null)]
    [InlineData("C:\\x\\y.d\\oc", null)]
    [InlineData("umlaut.dökx", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_extension_keeps_only_short_plain_endings(string? fileName, string? expected)
    {
        Assert.Equal(expected, SourceRefusalCodes.ExtensionOf(fileName));
    }

    [Theory]
    [InlineData("application/pdf", "application/pdf")]
    [InlineData("Text/Plain; charset=utf-8", "text/plain")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("image/svg+xml", "image/svg+xml")]
    [InlineData("pdf", null)]
    [InlineData("a/b/c", null)]
    [InlineData("text/ plain", null)]
    [InlineData("text/<script>", null)]
    [InlineData("/plain", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_declared_type_keeps_only_clean_media_types(string? declared, string? expected)
    {
        Assert.Equal(expected, SourceRefusalCodes.DeclaredTypeOf(declared));
    }

    [Fact]
    public void A_declared_type_longer_than_its_column_is_left_out()
    {
        Assert.Null(SourceRefusalCodes.DeclaredTypeOf("application/" + new string('x', 100)));
    }

    [Theory]
    [InlineData(RefusalPath.Web, "web")]
    [InlineData(RefusalPath.McpInline, "mcp_inline")]
    [InlineData(RefusalPath.McpTicket, "mcp_ticket")]
    [InlineData(RefusalPath.UploadPage, "upload_page")]
    public void Ways_are_stored_with_their_fixed_names(RefusalPath path, string name)
    {
        Assert.Equal(name, path.ToStorageName());
        Assert.Equal(path, RefusalPathNames.FromStorageName(name));
    }

    private static IEnumerable<FieldInfo> ReasonFields() =>
        typeof(SourceIntakeService).Assembly.GetTypes()
            .Where(t => t.Namespace?.StartsWith("Geef.Atelier.Infrastructure.Sources", StringComparison.Ordinal) == true)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.EndsWith("Reason", StringComparison.Ordinal));
}
