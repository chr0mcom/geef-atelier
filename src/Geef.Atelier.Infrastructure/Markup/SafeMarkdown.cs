using Markdig;
using Markdig.Extensions.AutoIdentifiers;
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

    /// <summary>
    /// Adds the extensions that are safe for texts nobody has reviewed. Headings get the same anchors as on GitHub (letters such as
    /// umlauts stay, every space becomes a hyphen), so that links to a section of a document work on both.
    /// </summary>
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
            .UseAutoIdentifiers(AutoIdentifierOptions.AutoLink | AutoIdentifierOptions.GitHub);

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

    /// <summary>True when <paramref name="url"/> is an absolute address with one of <see cref="AllowedSchemes"/>.</summary>
    public static bool IsAllowed(string? url) =>
        !string.IsNullOrWhiteSpace(url) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && AllowedSchemes.Contains(uri.Scheme);

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
