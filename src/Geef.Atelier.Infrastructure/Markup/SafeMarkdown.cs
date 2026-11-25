using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.Abbreviations;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Geef.Atelier.Infrastructure.Markup;

/// <summary>
/// The only Markdown configuration of the Atelier. Texts come from models and may carry words of uploaded files or web pages, so
/// the extensions are listed one by one: generic attributes (<c>{...}</c> becomes HTML attributes, including event handlers, and
/// placeholders such as <c>{Name}</c> disappear), media links, custom containers, diagrams and mathematics stay off.
/// </summary>
public static class SafeMarkdown
{
    /// <summary>Schemes a link may have in <see cref="ToSafeHtml"/>; anything else, relative addresses included, is shown as plain text.</summary>
    public static IReadOnlySet<string> AllowedSchemes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    /// <summary>Adds the extensions that are safe for texts nobody has reviewed.</summary>
    public static MarkdownPipelineBuilder UseSafeExtensions(this MarkdownPipelineBuilder builder) =>
        builder
            .UsePipeTables()
            .UseGridTables()
            .UseTaskLists()
            .UseListExtras()
            .UseEmphasisExtras()
            .UseAutoLinks()
            .UseFootnotes()
            .UseDefinitionLists()
            .UseAbbreviations()
            .UseAutoIdentifiers();

    /// <summary>
    /// The HTML of <paramref name="markdown"/> in which nothing acts on its own: links stay links only for
    /// <see cref="AllowedSchemes"/>, every other link keeps just its text, and no image is loaded (an image keeps just its
    /// alternative text). The pipeline must disable raw HTML.
    /// </summary>
    public static string ToSafeHtml(this string? markdown, MarkdownPipeline pipeline)
    {
        var document = Markdown.Parse(markdown ?? string.Empty, pipeline);
        foreach (var link in document.Descendants<LinkInline>().ToList())
        {
            if (!link.IsImage && IsAllowed(link.Url))
                continue;
            Unwrap(link);
        }
        foreach (var link in document.Descendants<AutolinkInline>().ToList())
        {
            if (IsAllowed(link.IsEmail ? "mailto:" + link.Url : link.Url))
                continue;
            link.ReplaceBy(new LiteralInline(link.Url ?? string.Empty));
        }

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>
    /// The plain text of <paramref name="markdown"/> on one line, parsed with the same <paramref name="pipeline"/> as
    /// <see cref="ToSafeHtml"/>: emphasis, bullets, heading marks and escapes are gone, the words stay. A link keeps its text, an
    /// image its alternative text, code its content; the number of an item in a numbered list stays (<c>2.</c>), so no figure is
    /// lost; link definitions and footnote marks are left out. Blanks and line breaks become one space. Only for
    /// display; stored texts keep their wording.
    /// </summary>
    public static string ToPlainText(this string? markdown, MarkdownPipeline pipeline)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;
        var builder = new StringBuilder(markdown.Length);
        AppendBlock(builder, Markdown.Parse(markdown, pipeline));
        return string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>True when <paramref name="url"/> is an absolute address with one of <see cref="AllowedSchemes"/>.</summary>
    public static bool IsAllowed(string? url) =>
        !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && AllowedSchemes.Contains(uri.Scheme);

    private static void AppendBlock(StringBuilder builder, Block block)
    {
        switch (block)
        {
            case LinkReferenceDefinitionGroup or LinkReferenceDefinition or ThematicBreakBlock:
                return;
            case ListItemBlock { Parent: ListBlock { IsOrdered: true } list } item:
                builder.Append(' ').Append(item.Order.ToString(CultureInfo.InvariantCulture)).Append(list.OrderedDelimiter).Append(' ');
                break;
            case LeafBlock { Inline: { } inline }:
                builder.Append(' ');
                AppendInline(builder, inline);
                builder.Append(' ');
                return;
            case LeafBlock leaf:
                builder.Append(' ').Append(leaf.Lines.ToString()).Append(' ');
                return;
        }
        if (block is ContainerBlock container)
        {
            foreach (var child in container)
                AppendBlock(builder, child);
        }
    }

    private static void AppendInline(StringBuilder builder, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content.AsSpan());
                return;
            case CodeInline code:
                builder.Append(code.Content);
                return;
            case AutolinkInline autolink:
                builder.Append(autolink.Url);
                return;
            case HtmlEntityInline entity:
                builder.Append(entity.Transcoded.AsSpan());
                return;
            case AbbreviationInline abbreviation:
                builder.Append(abbreviation.Abbreviation.Label);
                return;
            case HtmlInline html:
                builder.Append(html.Tag);
                return;
            case LineBreakInline:
                builder.Append(' ');
                return;
            case DelimiterInline delimiter:
                builder.Append(delimiter.ToLiteral());
                break;
        }
        if (inline is ContainerInline container)
        {
            foreach (var child in container)
                AppendInline(builder, child);
        }
    }

    private static void Unwrap(ContainerInline link)
    {
        var child = link.FirstChild;
        while (child is not null)
        {
            var next = child.NextSibling;
            child.Remove();
            link.InsertBefore(child);
            child = next;
        }
        link.Remove();
    }
}
