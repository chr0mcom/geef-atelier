using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Geef.Atelier.Web.Components.UI;

/// <summary>
/// Renders Markdown written by a model (and possibly carrying words of other web pages) so that nothing in it acts on its own: links
/// stay links only for <see cref="AllowedSchemes"/>, every other link keeps just its text, and no image is loaded (an image keeps
/// just its alternative text).
/// </summary>
public static class MarkdownSafety
{
    /// <summary>Schemes a link may have; anything else, relative addresses included, is shown as plain text.</summary>
    public static IReadOnlySet<string> AllowedSchemes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    /// <summary>The HTML of <paramref name="markdown"/> under <paramref name="pipeline"/> after the rules of <see cref="MarkdownSafety"/>.</summary>
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
