using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Geef.Atelier.Infrastructure.Finalizers.FormatConverters;

internal static class MarkdownToDocxConverter
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static byte[] Convert(string markdown, string title = "")
    {
        var doc = Markdown.Parse(markdown, Pipeline);
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var mainPart = wordDoc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            if (!string.IsNullOrWhiteSpace(title))
                body.AppendChild(BuildHeading(title, 1));

            foreach (var block in doc)
                AppendBlock(body, block);

            // Required section properties to close the document
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }
        return ms.ToArray();
    }

    private static void AppendBlock(Body body, Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
                body.AppendChild(BuildHeading(ExtractText(h.Inline), h.Level));
                break;

            case ParagraphBlock p:
                body.AppendChild(BuildParagraph(p.Inline));
                break;

            case FencedCodeBlock fcb:
                body.AppendChild(BuildCodeParagraph(fcb.Lines.ToString()));
                break;

            case CodeBlock cb:
                body.AppendChild(BuildCodeParagraph(cb.Lines.ToString()));
                break;

            case ListBlock lb:
                AppendList(body, lb, 0);
                break;

            case QuoteBlock qb:
                foreach (var b in qb)
                    AppendBlock(body, b);
                break;

            case ThematicBreakBlock:
                body.AppendChild(BuildHorizontalRule());
                break;
        }
    }

    private static void AppendList(Body body, ListBlock list, int depth)
    {
        int index = 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock li) continue;
            foreach (var b in li)
            {
                if (b is ParagraphBlock p)
                {
                    var para = BuildParagraph(p.Inline);
                    var props = para.PrependChild(new ParagraphProperties());
                    props.AppendChild(new Indentation
                    {
                        Left = ((depth + 1) * 360).ToString()
                    });
                    var numText = list.IsOrdered ? $"{index}. " : "• ";
                    para.PrependChild(new Run(new Text(numText) { Space = SpaceProcessingModeValues.Preserve }));
                    body.AppendChild(para);
                }
                else if (b is ListBlock nested)
                {
                    AppendList(body, nested, depth + 1);
                }
            }
            index++;
        }
    }

    private static Paragraph BuildHeading(string text, int level)
    {
        var styleName = level switch
        {
            1 => "Heading1",
            2 => "Heading2",
            3 => "Heading3",
            _ => "Heading4",
        };
        var para = new Paragraph();
        para.AppendChild(new ParagraphProperties(
            new ParagraphStyleId { Val = styleName }));
        para.AppendChild(new Run(new Text(text)));
        return para;
    }

    private static Paragraph BuildParagraph(ContainerInline? container)
    {
        var para = new Paragraph();
        if (container is null) return para;
        foreach (var inline in container)
            AppendInline(para, inline);
        return para;
    }

    private static void AppendInline(Paragraph para, Markdig.Syntax.Inlines.Inline inline)
    {
        switch (inline)
        {
            case LiteralInline lit:
                para.AppendChild(new Run(new Text(lit.Content.ToString())
                    { Space = SpaceProcessingModeValues.Preserve }));
                break;

            case EmphasisInline em:
            {
                var text = ExtractText(em);
                var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
                var props = new RunProperties();
                if (em.DelimiterCount >= 2) props.AppendChild(new Bold());
                if (em.DelimiterCount == 1) props.AppendChild(new Italic());
                if (props.HasChildren) run.PrependChild(props);
                para.AppendChild(run);
                break;
            }

            case CodeInline code:
            {
                var run = new Run(new Text(code.Content) { Space = SpaceProcessingModeValues.Preserve });
                run.PrependChild(new RunProperties(new RunFonts { Ascii = "Courier New" }));
                para.AppendChild(run);
                break;
            }

            case LinkInline link:
            {
                var run = new Run(new Text(ExtractText(link))
                    { Space = SpaceProcessingModeValues.Preserve });
                run.PrependChild(new RunProperties(new Underline { Val = UnderlineValues.Single }));
                para.AppendChild(run);
                break;
            }

            case LineBreakInline lb:
                if (lb.IsHard)
                    para.AppendChild(new Run(new Break()));
                else
                    para.AppendChild(new Run(new Text(" ") { Space = SpaceProcessingModeValues.Preserve }));
                break;

            case ContainerInline container:
                foreach (var child in container)
                    AppendInline(para, child);
                break;
        }
    }

    private static Paragraph BuildCodeParagraph(string code)
    {
        var para = new Paragraph();
        var props = new ParagraphProperties();
        props.AppendChild(new ParagraphStyleId { Val = "NoSpacing" });
        para.AppendChild(props);

        foreach (var line in code.Split('\n'))
        {
            var run = new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
            run.PrependChild(new RunProperties(new RunFonts { Ascii = "Courier New" }, new FontSize { Val = "18" }));
            para.AppendChild(run);
            para.AppendChild(new Run(new Break()));
        }
        return para;
    }

    private static Paragraph BuildHorizontalRule()
    {
        var para = new Paragraph();
        var props = new ParagraphProperties();
        var border = new ParagraphBorders();
        border.AppendChild(new BottomBorder
        {
            Val = BorderValues.Single,
            Size = 6,
            Space = 1,
            Color = "888888"
        });
        props.AppendChild(border);
        para.AppendChild(props);
        return para;
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
}
