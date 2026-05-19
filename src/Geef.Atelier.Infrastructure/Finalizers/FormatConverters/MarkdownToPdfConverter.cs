using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Geef.Atelier.Infrastructure.Finalizers.FormatConverters;

internal static class MarkdownToPdfConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    static MarkdownToPdfConverter()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static byte[] Convert(string markdown, string title = "")
    {
        var doc = Markdown.Parse(markdown, Pipeline);
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(2, Unit.Centimetre);
                page.DefaultTextStyle(t => t.FontSize(11).FontFamily("Arial"));

                if (!string.IsNullOrWhiteSpace(title))
                {
                    page.Header()
                        .PaddingBottom(8)
                        .Text(title)
                        .FontSize(9)
                        .FontColor(Colors.Grey.Medium);
                }

                page.Footer()
                    .AlignCenter()
                    .Text(t =>
                    {
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });

                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    foreach (var block in doc)
                        RenderBlock(col, block);
                });
            });
        }).GeneratePdf();
    }

    private static void RenderBlock(ColumnDescriptor col, Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
                col.Item().PaddingTop(h.Level <= 2 ? 10 : 6).Text(t =>
                {
                    t.Span(ExtractText(h.Inline))
                        .FontSize(HeadingFontSize(h.Level))
                        .Bold();
                });
                break;

            case ParagraphBlock p:
                col.Item().Text(t => RenderInlines(t, p.Inline));
                break;

            case FencedCodeBlock fcb:
                col.Item()
                    .Background(Colors.Grey.Lighten3)
                    .Padding(8)
                    .Text(fcb.Lines.ToString())
                    .FontFamily("Courier New")
                    .FontSize(9);
                break;

            case CodeBlock cb:
                col.Item()
                    .Background(Colors.Grey.Lighten3)
                    .Padding(8)
                    .Text(cb.Lines.ToString())
                    .FontFamily("Courier New")
                    .FontSize(9);
                break;

            case ListBlock lb:
                RenderList(col, lb, 0);
                break;

            case QuoteBlock qb:
                col.Item()
                    .BorderLeft(3)
                    .BorderColor(Colors.Grey.Medium)
                    .PaddingLeft(10)
                    .Column(inner =>
                    {
                        inner.Spacing(4);
                        foreach (var b in qb)
                            RenderBlock(inner, b);
                    });
                break;

            case ThematicBreakBlock:
                col.Item().PaddingVertical(4).Height(1).Background(Colors.Grey.Lighten2);
                break;
        }
    }

    private static void RenderList(ColumnDescriptor col, ListBlock list, int depth)
    {
        int index = 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock li) continue;
            var indent = depth * 16f;
            var bullet = list.IsOrdered ? $"{index++}." : "•";
            col.Item().PaddingLeft(indent).Row(row =>
            {
                row.ConstantItem(16).Text(bullet).FontSize(11);
                row.RelativeItem().Column(inner =>
                {
                    inner.Spacing(2);
                    foreach (var b in li)
                    {
                        if (b is ParagraphBlock p)
                            inner.Item().Text(t => RenderInlines(t, p.Inline));
                        else if (b is ListBlock nested)
                            RenderList(inner, nested, depth + 1);
                    }
                });
            });
        }
    }

    private static void RenderInlines(TextDescriptor text, ContainerInline? container)
    {
        if (container is null) return;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    text.Span(lit.Content.ToString());
                    break;

                case EmphasisInline em:
                {
                    var content = ExtractText(em);
                    var span = text.Span(content);
                    if (em.DelimiterCount >= 2) span.Bold();
                    if (em.DelimiterChar is '*' or '_' && em.DelimiterCount == 1)
                        span.Italic();
                    break;
                }

                case CodeInline code:
                    text.Span(code.Content)
                        .FontFamily("Courier New")
                        .FontSize(9)
                        .BackgroundColor(Colors.Grey.Lighten3);
                    break;

                case LinkInline link:
                    text.Span(ExtractText(link))
                        .FontColor(Colors.Blue.Medium);
                    break;

                case LineBreakInline lb when lb.IsHard:
                    text.Line("");
                    break;

                case LineBreakInline:
                    text.Span(" ");
                    break;
            }
        }
    }

    private static string ExtractText(ContainerInline? container)
    {
        if (container is null) return string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit: sb.Append(lit.Content); break;
                case EmphasisInline em: sb.Append(ExtractText(em)); break;
                case CodeInline code: sb.Append(code.Content); break;
                case LinkInline link: sb.Append(ExtractText(link)); break;
                case LineBreakInline: sb.Append(' '); break;
            }
        }
        return sb.ToString();
    }

    private static float HeadingFontSize(int level) => level switch
    {
        1 => 22f,
        2 => 18f,
        3 => 15f,
        4 => 13f,
        _ => 12f,
    };
}
