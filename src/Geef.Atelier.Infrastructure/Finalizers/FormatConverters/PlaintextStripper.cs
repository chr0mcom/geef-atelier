using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;

namespace Geef.Atelier.Infrastructure.Finalizers.FormatConverters;

internal static class PlaintextStripper
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string Strip(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        var sb = new StringBuilder();
        AppendBlocks(sb, document);
        return sb.ToString().TrimEnd();
    }

    private static void AppendBlocks(StringBuilder sb, IEnumerable<Block> blocks)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock h:
                    AppendInlines(sb, h.Inline);
                    sb.AppendLine();
                    break;
                case ParagraphBlock p:
                    AppendInlines(sb, p.Inline);
                    sb.AppendLine();
                    sb.AppendLine();
                    break;
                case FencedCodeBlock fcb:
                    sb.AppendLine(fcb.Lines.ToString());
                    sb.AppendLine();
                    break;
                case CodeBlock cb:
                    sb.AppendLine(cb.Lines.ToString());
                    sb.AppendLine();
                    break;
                case ListBlock lb:
                    AppendList(sb, lb, 0);
                    sb.AppendLine();
                    break;
                case QuoteBlock qb:
                    AppendBlocks(sb, qb);
                    break;
                case ThematicBreakBlock:
                    sb.AppendLine("---");
                    sb.AppendLine();
                    break;
            }
        }
    }

    private static void AppendList(StringBuilder sb, ListBlock list, int depth)
    {
        int index = 1;
        foreach (var item in list)
        {
            if (item is not ListItemBlock li) continue;
            var indent = new string(' ', depth * 2);
            var bullet = list.IsOrdered ? $"{index++}." : "-";
            sb.Append(indent).Append(bullet).Append(' ');
            bool first = true;
            foreach (var block in li)
            {
                if (block is ParagraphBlock p)
                {
                    if (!first) sb.Append(indent).Append("  ");
                    AppendInlines(sb, p.Inline);
                    sb.AppendLine();
                    first = false;
                }
                else if (block is ListBlock nested)
                {
                    AppendList(sb, nested, depth + 1);
                }
            }
        }
    }

    private static void AppendInlines(StringBuilder sb, ContainerInline? container)
    {
        if (container is null) return;
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    sb.Append(lit.Content);
                    break;
                case EmphasisInline em:
                    AppendInlines(sb, em);
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case LinkInline link:
                    AppendInlines(sb, link);
                    break;
                case LineBreakInline lb:
                    sb.Append(lb.IsHard ? '\n' : ' ');
                    break;
            }
        }
    }
}
