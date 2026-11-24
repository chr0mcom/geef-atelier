using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace Geef.Atelier.Tests.TestData;

/// <summary>Builds synthetic PDF and Word documents in memory for source extraction tests.</summary>
internal static class SyntheticDocuments
{
    private const double PageWidth = 595;
    private const double PageHeight = 842;
    private const double Margin = 56;
    private const double FontSize = 10;
    private const double LineHeight = 13;

    /// <summary>
    /// Builds a PDF with <paramref name="pages"/> pages; <paramref name="pageText"/> returns the text of each 1-based page.
    /// Lines (split at <c>\n</c>) are wrapped to the page width; an empty string gives a page without text. A page text
    /// starting with <see cref="TwoColumns"/> is laid out in two columns separated by <c>||</c>.
    /// </summary>
    public static byte[] Pdf(int pages, Func<int, string> pageText)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pages, 1);
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);

        for (var number = 1; number <= pages; number++)
        {
            var page = builder.AddPage(PageWidth, PageHeight);
            var text = pageText(number) ?? string.Empty;
            if (text.StartsWith(TwoColumns, StringComparison.Ordinal))
            {
                var parts = text[TwoColumns.Length..].Split("||");
                var columnWidth = (PageWidth - 3 * Margin) / 2;
                WriteLines(page, font, parts[0], Margin, columnWidth);
                WriteLines(page, font, parts.Length > 1 ? parts[1] : string.Empty, 2 * Margin + columnWidth, columnWidth);
            }
            else
            {
                WriteLines(page, font, text, Margin, PageWidth - 2 * Margin);
            }
        }

        return builder.Build();
    }

    /// <summary>Prefix of a page text that is laid out in two columns; the columns are separated by <c>||</c>.</summary>
    public const string TwoColumns = "<two-columns>";

    /// <summary>
    /// Builds a PDF that is protected with a user password, so it cannot be opened without it (a standard security
    /// handler whose check values never match the empty password).
    /// </summary>
    public static byte[] EncryptedPdf()
    {
        var plain = Encoding.Latin1.GetString(Pdf(1, _ => "Secret text."));
        var trailer = plain.LastIndexOf("trailer", StringComparison.Ordinal);
        var open = plain.IndexOf("<<", trailer, StringComparison.Ordinal);
        var hex = new string('A', 64);
        var encrypt = $"/Encrypt << /Filter /Standard /V 1 /R 2 /O <{hex}> /U <{hex}> /P -4 >> /ID [<{new string('B', 32)}> <{new string('B', 32)}>] ";
        return Encoding.Latin1.GetBytes(plain.Insert(open + 2, encrypt));
    }

    /// <summary>
    /// Builds a Word document with one paragraph per entry of <paramref name="paragraphs"/>. Conventions: <c># text</c>
    /// is a heading (level = number of <c>#</c>), <c>- text</c> a list item, consecutive entries <c>| a | b |</c> form a
    /// table, and <c>[picture]</c> adds a paragraph with an inline picture.
    /// </summary>
    public static byte[] Docx(IReadOnlyList<string> paragraphs)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new Document(new Body());
            var body = main.Document.Body!;
            AddHeader(main);

            Table? table = null;
            foreach (var entry in paragraphs)
            {
                if (entry.StartsWith('|'))
                {
                    if (table is null)
                    {
                        table = new Table();
                        body.Append(table);
                    }

                    var row = new TableRow();
                    foreach (var cell in entry.Trim('|').Split('|'))
                        row.Append(new TableCell(new Paragraph(new Run(new Text(cell.Trim())))));
                    table.Append(row);
                    continue;
                }

                table = null;
                if (entry == "[picture]")
                {
                    body.Append(new Paragraph(new Run(new Drawing())));
                    continue;
                }

                var level = entry.TakeWhile(c => c == '#').Count();
                if (level > 0 && entry.Length > level && entry[level] == ' ')
                {
                    body.Append(new Paragraph(
                        new ParagraphProperties(new ParagraphStyleId { Val = $"Heading{level}" }),
                        new Run(new Text(entry[(level + 1)..]))));
                }
                else if (entry.StartsWith("- ", StringComparison.Ordinal))
                {
                    body.Append(new Paragraph(
                        new ParagraphProperties(new NumberingProperties(new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 1 })),
                        new Run(new Text(entry[2..]))));
                }
                else
                {
                    body.Append(new Paragraph(new Run(new Text(entry) { Space = SpaceProcessingModeValues.Preserve })));
                }
            }

            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static void AddHeader(MainDocumentPart main)
    {
        var header = main.AddNewPart<HeaderPart>();
        header.Header = new Header(new Paragraph(new Run(new Text("HEADER-ONLY-TEXT"))));
        header.Header.Save();
    }

    private static void WriteLines(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string text, double left, double width)
    {
        var maxChars = Math.Max(10, (int)(width / (FontSize * 0.5)));
        var y = PageHeight - Margin;
        foreach (var paragraph in text.Split('\n'))
        {
            foreach (var line in Wrap(paragraph, maxChars))
            {
                if (y < Margin)
                    return;
                if (line.Length > 0)
                    page.AddText(line, FontSize, new PdfPoint(left, y), font);
                y -= LineHeight;
            }
        }
    }

    private static IEnumerable<string> Wrap(string paragraph, int maxChars)
    {
        if (paragraph.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var line = new StringBuilder();
        foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.Length > 0 && line.Length + 1 + word.Length > maxChars)
            {
                yield return line.ToString();
                line.Clear();
            }

            if (line.Length > 0)
                line.Append(' ');
            line.Append(word);
        }

        if (line.Length > 0)
            yield return line.ToString();
    }
}
