using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace Geef.Atelier.Infrastructure.Security;

internal sealed class AngleSharpHtmlContentExtractor : IHtmlContentExtractor
{
    private static readonly string[] BoilerplateTags =
        ["script", "style", "nav", "header", "footer", "aside", "iframe", "noscript", "form"];

    private static readonly Regex WhitespaceRegex =
        new(@"\s{2,}", RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    public Task<HtmlExtractionResult> ExtractAsync(string html, bool stripBoilerplate, CancellationToken ct = default)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var title = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title']")?.GetAttributeValue("content", string.Empty)
            ?? doc.DocumentNode.SelectSingleNode("//title")?.InnerText?.Trim();

        if (stripBoilerplate)
        {
            foreach (var tag in BoilerplateTags)
            {
                var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
                if (nodes is not null)
                    foreach (var node in nodes.ToList())
                        node.Remove();
            }
        }

        var body = doc.DocumentNode.SelectSingleNode("//body");
        if (body is null)
            return Task.FromResult(new HtmlExtractionResult(title, string.Empty));

        var text = HtmlEntity.DeEntitize(body.InnerText);
        text = WhitespaceRegex.Replace(text, " ").Trim();

        return Task.FromResult(new HtmlExtractionResult(title, text));
    }
}
