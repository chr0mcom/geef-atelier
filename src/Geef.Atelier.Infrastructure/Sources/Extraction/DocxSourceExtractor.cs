using System.Globalization;
using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>
/// Reads the body of a Word file as Markdown: headings as <c>#</c>, list items as <c>- </c>, tables row by row with
/// <c> | </c>. Headers, footers, comments, deleted text and pictures are left out; pictures raise a warning. The archive
/// is checked against entry count, total size and compression ratio before it is opened.
/// </summary>
internal sealed class DocxSourceExtractor(IOptions<SourceOptions> options, ILogger<DocxSourceExtractor> logger) : ISourceTextExtractor
{
    internal const string BrokenReason = "This file could not be read as a Word document.";
    internal const string ArchiveReason = "This Word file is unusually large inside and was not opened.";
    internal const string NoTextReason = "This Word file has no text.";
    private const long RatioCheckFromBytes = 1_048_576;

    public SourceKind Kind => SourceKind.Docx;

    public Task<SourceExtraction> ExtractAsync(ReadOnlyMemory<byte> content, CancellationToken ct) =>
        Task.Run(() => Extract(content.ToArray()), ct);

    private SourceExtraction Extract(byte[] bytes)
    {
        if (!ArchiveIsSafe(bytes, options.Value))
            return SourceExtraction.Unreadable(ArchiveReason);

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var document = WordprocessingDocument.Open(stream, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
                return SourceExtraction.Unreadable(NoTextReason);

            var state = new WalkState();
            Walk(body, state);
            var text = state.Builder.ToString().Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
            if (text.Length == 0)
                return SourceExtraction.Unreadable(NoTextReason);

            var warnings = new List<SourceWarning>();
            if (state.Pictures > 0)
                warnings.Add(new SourceWarning(SourceWarningCodes.DocxContentSkipped, state.Pictures == 1
                    ? "1 picture was skipped; only the text is used."
                    : string.Create(CultureInfo.InvariantCulture, $"{state.Pictures} pictures were skipped; only the text is used.")));

            return new SourceExtraction(text, null, [], SourceQuality.Good, warnings, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation("Word file could not be read: {Error}", ex.GetType().Name);
            return SourceExtraction.Unreadable(BrokenReason);
        }
    }

    internal static bool ArchiveIsSafe(byte[] bytes, SourceOptions options)
    {
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            if (archive.Entries.Count > options.MaxArchiveEntries)
                return false;

            long total = 0;
            foreach (var entry in archive.Entries)
            {
                total += entry.Length;
                if (total > options.MaxUncompressedBytes)
                    return false;
                if (entry.Length > RatioCheckFromBytes && entry.Length > (long)options.MaxCompressionRatio * Math.Max(1, entry.CompressedLength))
                    return false;
            }

            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static void Walk(OpenXmlElement container, WalkState state)
    {
        foreach (var element in container.ChildElements)
        {
            switch (element)
            {
                case Paragraph paragraph:
                    AppendParagraph(paragraph, state);
                    break;
                case Table table:
                    AppendTable(table, state);
                    break;
                case SectionProperties:
                    break;
                default:
                    Walk(element, state);
                    break;
            }
        }
    }

    private static void AppendParagraph(Paragraph paragraph, WalkState state)
    {
        state.Pictures += CountPictures(paragraph);
        var text = ParagraphText(paragraph);
        if (text.Trim().Length == 0)
            return;

        var prefix = HeadingLevel(paragraph) is { } level
            ? new string('#', level) + " "
            : IsListItem(paragraph) ? "- " : string.Empty;
        AppendBlock(state, prefix + text.Trim());
    }

    private static void AppendTable(Table table, WalkState state)
    {
        var rows = new List<string>();
        var first = true;
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>()
                .Select(cell =>
                {
                    state.Pictures += CountPictures(cell);
                    return string.Join(' ', cell.Descendants<Paragraph>().Select(ParagraphText).Where(t => t.Trim().Length > 0).Select(t => t.Trim()))
                        .Replace('|', '/');
                })
                .ToList();
            if (cells.Count == 0)
                continue;

            rows.Add("| " + string.Join(" | ", cells) + " |");
            if (first)
                rows.Add("|" + string.Concat(Enumerable.Repeat(" --- |", cells.Count)));
            first = false;
        }

        if (rows.Count > 0)
            AppendBlock(state, string.Join('\n', rows));
    }

    private static string ParagraphText(Paragraph paragraph)
    {
        var builder = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            switch (element)
            {
                case Text text when text.Parent is not null && !IsInsideTable(text, paragraph):
                    builder.Append(text.Text);
                    break;
                case TabChar:
                    builder.Append(' ');
                    break;
                case Break or CarriageReturn:
                    builder.Append(' ');
                    break;
            }
        }

        return builder.ToString();
    }

    private static bool IsInsideTable(OpenXmlElement element, Paragraph paragraph)
    {
        for (var current = element.Parent; current is not null && current != paragraph; current = current.Parent)
        {
            if (current is Table)
                return true;
        }

        return false;
    }

    private static int? HeadingLevel(Paragraph paragraph)
    {
        var style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (style is not null)
        {
            if (style.Equals("Title", StringComparison.OrdinalIgnoreCase))
                return 1;
            foreach (var prefix in new[] { "Heading", "berschrift" })
            {
                var index = style.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
                if (index >= 0 && int.TryParse(style.AsSpan(index + prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var level) && level is >= 1 and <= 6)
                    return level;
            }
        }

        var outline = paragraph.ParagraphProperties?.OutlineLevel?.Val?.Value;
        return outline is >= 0 and <= 5 ? outline + 1 : null;
    }

    private static bool IsListItem(Paragraph paragraph)
    {
        var properties = paragraph.ParagraphProperties;
        if (properties?.NumberingProperties is not null)
            return true;
        var style = properties?.ParagraphStyleId?.Val?.Value;
        return style is not null && style.Contains("List", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountPictures(OpenXmlElement element) =>
        element.Descendants<Drawing>().Count() + element.Descendants<Picture>().Count();

    private static void AppendBlock(WalkState state, string block)
    {
        if (state.Builder.Length > 0)
            state.Builder.Append(state.LastWasList && block.StartsWith("- ", StringComparison.Ordinal) ? "\n" : "\n\n");
        state.Builder.Append(block);
        state.LastWasList = block.StartsWith("- ", StringComparison.Ordinal);
    }

    private sealed class WalkState
    {
        public StringBuilder Builder { get; } = new();
        public int Pictures { get; set; }
        public bool LastWasList { get; set; }
    }
}
