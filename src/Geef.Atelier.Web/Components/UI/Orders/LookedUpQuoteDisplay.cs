using System.Text.RegularExpressions;
using Geef.Atelier.Infrastructure.Markup;
using Markdig;

namespace Geef.Atelier.Web.Components.UI.Orders;

/// <summary>
/// How a passage under "Looked up" is shown on the web page: without the marks of Markdown (<c>**</c>, <c>1\.</c>, bullets, heading
/// marks), on one line, through <see cref="SafeMarkdown.ToPlainText"/>. Only the display changes; the stored passage, the material
/// of the writer and the fact check and the passage in MCP keep their wording.
/// </summary>
public static partial class LookedUpQuoteDisplay
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseSafeExtensions().DisableHtml().Build();

    /// <summary>
    /// <paramref name="quote"/> as plain text; unchanged when the plain text would be blank or its figures (the runs of digits in
    /// their order) differ from those of the passage, so a figure is never lost or changed by the display.
    /// </summary>
    public static string ToShownQuote(this string? quote)
    {
        if (string.IsNullOrWhiteSpace(quote))
            return quote ?? string.Empty;
        var plain = quote.ToPlainText(Pipeline);
        return plain.Length > 0 && SameFigures(plain, quote) ? plain : quote;
    }

    private static bool SameFigures(string plain, string quote) =>
        Figures().Matches(plain).Select(m => m.Value).SequenceEqual(Figures().Matches(quote).Select(m => m.Value), StringComparer.Ordinal);

    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex Figures();
}
