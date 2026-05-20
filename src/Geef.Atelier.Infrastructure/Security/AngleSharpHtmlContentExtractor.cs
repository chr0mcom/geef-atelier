using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharpConfig = AngleSharp.Configuration;

namespace Geef.Atelier.Infrastructure.Security;

internal sealed class AngleSharpHtmlContentExtractor : IHtmlContentExtractor
{
    private static readonly string[] BoilerplateTags =
        ["script", "style", "nav", "header", "footer", "aside", "iframe", "noscript", "form"];

    public async Task<HtmlExtractionResult> ExtractAsync(string html, bool stripBoilerplate, CancellationToken ct = default)
    {
        var context = BrowsingContext.New(AngleSharpConfig.Default);
        var parser = context.GetService<IHtmlParser>()!;
        var document = await parser.ParseDocumentAsync(html, ct);

        var title = document.QuerySelector("meta[property='og:title']")?.GetAttribute("content")
            ?? document.QuerySelector("title")?.TextContent?.Trim();

        if (stripBoilerplate)
        {
            foreach (var tag in BoilerplateTags)
            {
                foreach (var el in document.QuerySelectorAll(tag).ToList())
                    el.Remove();
            }
        }

        var body = document.Body;
        if (body is null)
            return new HtmlExtractionResult(title, string.Empty);

        var text = body.TextContent;

        // Normalize whitespace
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s{2,}", " ").Trim();

        return new HtmlExtractionResult(title, text);
    }
}
