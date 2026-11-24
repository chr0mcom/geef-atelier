using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
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
/// <c> | </c>. Headers, footers, comments, deleted text and pictures are left out; pictures raise a warning. Before the
/// file is opened, the archive is checked against entry count, total size and compression ratio, and every XML part is
/// read once as a stream without a DTD and with a nesting limit, so no deep or oversized part reaches the document model.
/// The same pass counts the characters of all text runs; above <see cref="SourceOptions.MaxExtractedChars"/> the file is
/// refused before the document model is built. The body is walked without recursion and stops at the same limit.
/// </summary>
internal sealed class DocxSourceExtractor(IOptions<SourceOptions> options, ILogger<DocxSourceExtractor> logger) : ISourceTextExtractor
{
    internal const string BrokenReason = "This file could not be read as a Word document.";
    internal const string ArchiveReason = "This Word file is unusually large inside and was not opened.";
    internal const string NoTextReason = "This Word file has no text.";
    internal const string NestingReason = "This Word file has too many nested parts and was not opened.";
    internal const long RatioCheckFromBytes = 65_536;
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    public SourceKind Kind => SourceKind.Docx;

    public Task<SourceExtraction> ExtractAsync(ReadOnlyMemory<byte> content, CancellationToken ct) =>
        Task.Run(() => Extract(content.AsArray(), ct), ct);

    private SourceExtraction Extract(byte[] bytes, CancellationToken ct)
    {
        if (!ArchiveIsSafe(bytes, options.Value))
            return SourceExtraction.Unreadable(ArchiveReason);

        switch (ScanXmlParts(bytes, options.Value))
        {
            case XmlScanResult.TooDeep:
                return SourceExtraction.Unreadable(NestingReason);
            case XmlScanResult.Broken:
                return SourceExtraction.Unreadable(BrokenReason);
            case XmlScanResult.TooMuchText:
                return SourceExtraction.TooMuchText();
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var document = WordprocessingDocument.Open(stream, false);
            var body = document.MainDocumentPart?.Document?.Body;
            if (body is null)
                return SourceExtraction.Unreadable(NoTextReason);

            var state = new WalkState(Math.Max(1, options.Value.MaxExtractedChars));
            Walk(body, state, ct);
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
        catch (TextLimitExceededException)
        {
            return SourceExtraction.TooMuchText();
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or OutOfMemoryException))
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

    /// <summary>
    /// Reads every XML part of the archive once as a stream: no DTD, at most <see cref="SourceOptions.MaxUncompressedBytes"/>
    /// characters and at most <see cref="SourceOptions.MaxXmlDepth"/> levels of nesting. The characters inside Word text
    /// runs (<c>w:t</c>) of all parts are counted in chunks; above <see cref="SourceOptions.MaxExtractedChars"/> the scan
    /// stops with <see cref="XmlScanResult.TooMuchText"/>. Parts that do not start with <c>&lt;</c> (pictures and other
    /// binary parts) are skipped.
    /// </summary>
    internal static XmlScanResult ScanXmlParts(byte[] bytes, SourceOptions options)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = Math.Max(1, options.MaxUncompressedBytes),
            MaxCharactersFromEntities = 0,
            IgnoreComments = true,
            IgnoreWhitespace = true,
            IgnoreProcessingInstructions = true,
        };

        var maxTextChars = Math.Max(1L, options.MaxExtractedChars);
        var textChars = 0L;
        var chunk = new char[4_096];
        try
        {
            using var archive = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0 || !LooksLikeXml(entry))
                    continue;

                using var stream = entry.Open();
                using var reader = XmlReader.Create(stream, settings);
                var inText = false;
                while (reader.Read())
                {
                    if (reader.Depth > options.MaxXmlDepth)
                        return XmlScanResult.TooDeep;

                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            inText = !reader.IsEmptyElement && reader.LocalName == "t" && reader.NamespaceURI == WordNamespace;
                            break;
                        case XmlNodeType.EndElement:
                            inText = false;
                            break;
                        case XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace when inText:
                            textChars += CountValue(reader, chunk, maxTextChars - textChars);
                            if (textChars > maxTextChars)
                                return XmlScanResult.TooMuchText;
                            break;
                    }
                }
            }

            return XmlScanResult.Fine;
        }
        catch (Exception ex) when (ex is XmlException or InvalidDataException or IOException)
        {
            return XmlScanResult.Broken;
        }
    }

    private static long CountValue(XmlReader reader, char[] chunk, long allowed)
    {
        if (!reader.CanReadValueChunk)
            return reader.Value.Length;

        long count = 0;
        int read;
        while ((read = reader.ReadValueChunk(chunk, 0, chunk.Length)) > 0)
        {
            count += read;
            if (count > allowed)
                break;
        }

        return count;
    }

    private static bool LooksLikeXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        Span<byte> head = stackalloc byte[64];
        var read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        foreach (var b in head[..read])
        {
            if (b is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n' or 0xEF or 0xBB or 0xBF or 0xFE or 0xFF or 0x00)
                continue;
            return b == (byte)'<';
        }

        return false;
    }

    private static void Walk(OpenXmlElement root, WalkState state, CancellationToken ct)
    {
        var pending = new Stack<IEnumerator<OpenXmlElement>>();
        pending.Push(((IEnumerable<OpenXmlElement>)root.ChildElements).GetEnumerator());
        while (pending.Count > 0)
        {
            var children = pending.Peek();
            if (!children.MoveNext())
            {
                children.Dispose();
                pending.Pop();
                continue;
            }

            ct.ThrowIfCancellationRequested();
            switch (children.Current)
            {
                case Paragraph paragraph:
                    AppendParagraph(paragraph, state);
                    break;
                case Table table:
                    AppendTable(table, state);
                    break;
                case SectionProperties:
                    break;
                case var other:
                    pending.Push(((IEnumerable<OpenXmlElement>)other.ChildElements).GetEnumerator());
                    break;
            }
        }
    }

    private static void AppendParagraph(Paragraph paragraph, WalkState state)
    {
        state.Pictures += CountPictures(paragraph);
        var text = ParagraphText(paragraph, state.MaxChars);
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
        var total = 0L;
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = row.Elements<TableCell>()
                .Select(cell =>
                {
                    state.Pictures += CountPictures(cell);
                    return string.Join(' ', cell.Descendants<Paragraph>().Select(p => ParagraphText(p, state.MaxChars)).Where(t => t.Trim().Length > 0).Select(t => t.Trim()))
                        .Replace('|', '/');
                })
                .ToList();
            if (cells.Count == 0)
                continue;

            var line = "| " + string.Join(" | ", cells) + " |";
            total += line.Length + 1;
            if (state.Builder.Length + total > state.MaxChars)
                throw new TextLimitExceededException();
            rows.Add(line);
            if (first)
                rows.Add("|" + string.Concat(Enumerable.Repeat(" --- |", cells.Count)));
            first = false;
        }

        if (rows.Count > 0)
            AppendBlock(state, string.Join('\n', rows));
    }

    private static string ParagraphText(Paragraph paragraph, int maxChars)
    {
        var builder = new StringBuilder();
        foreach (var element in paragraph.Descendants())
        {
            if (builder.Length > maxChars)
                throw new TextLimitExceededException();

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
        if (state.Builder.Length > state.MaxChars)
            throw new TextLimitExceededException();
    }

    internal enum XmlScanResult { Fine = 0, TooDeep = 1, Broken = 2, TooMuchText = 3 }

    private sealed class TextLimitExceededException() : Exception("The Word file holds more text than one source may have.");

    private sealed class WalkState(int maxChars)
    {
        public int MaxChars { get; } = maxChars;
        public StringBuilder Builder { get; } = new();
        public int Pictures { get; set; }
        public bool LastWasList { get; set; }
    }
}
