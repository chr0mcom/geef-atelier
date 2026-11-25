using System.Text;

namespace Geef.Atelier.Web.Components.UI.Orders;

/// <summary>
/// How a passage under "Looked up" is shown on the web page. Only two marks that real passages carried and that are nothing but
/// formatting go: a pair of <c>**</c> around text and the backslash that keeps a number from starting a list (<c>1\. </c>). Everything
/// else stays word for word, also other backslashes, bullets, <c>#</c>, <c>&gt;</c>, <c>~~</c>, tables and links, because there a mark
/// can carry meaning (a pattern, a minus, a struck price). No Markdown parser, linear time, never throws. Only the display changes;
/// the stored passage, the material of the writer and the fact check and the passage in MCP keep their wording.
/// </summary>
public static class LookedUpQuoteDisplay
{
    /// <summary>
    /// <paramref name="quote"/> without the pairs of <c>**</c> around text and without the backslash in <c>1\. </c> (a digit before it,
    /// the dot and then a blank or the end after it). A pair counts only when the text between the marks neither starts nor ends with a
    /// blank and no letter or digit touches the marks from outside (so <c>2**3</c> stays).
    /// </summary>
    public static string ToShownQuote(this string? quote)
    {
        if (string.IsNullOrEmpty(quote))
            return quote ?? string.Empty;

        var text = new StringBuilder(quote.Length);
        var marks = new List<int>();
        for (var i = 0; i < quote.Length; i++)
        {
            if (IsListNumberEscape(quote, i))
                continue;
            if (quote[i] == '*' && i + 1 < quote.Length && quote[i + 1] == '*')
            {
                marks.Add(text.Length);
                text.Append("**");
                i++;
                continue;
            }
            text.Append(quote[i]);
        }

        var removed = new HashSet<int>();
        for (var k = 0; k + 1 < marks.Count; k++)
        {
            int open = marks[k], close = marks[k + 1];
            if (!Pair(text, open, close))
                continue;
            removed.Add(open);
            removed.Add(close);
            k++;
        }
        if (removed.Count == 0)
            return text.ToString();

        var shown = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (removed.Contains(i))
                i++;
            else
                shown.Append(text[i]);
        }
        return shown.ToString();
    }

    private static bool Pair(StringBuilder text, int open, int close)
    {
        var start = open + 2;
        var end = close - 1;
        return end >= start
            && !char.IsWhiteSpace(text[start]) && !char.IsWhiteSpace(text[end])
            && (open == 0 || !char.IsLetterOrDigit(text[open - 1]))
            && (close + 2 >= text.Length || !char.IsLetterOrDigit(text[close + 2]));
    }

    private static bool IsListNumberEscape(string quote, int i) =>
        quote[i] == '\\' && i > 0 && char.IsAsciiDigit(quote[i - 1]) && i + 1 < quote.Length && quote[i + 1] == '.'
        && (i + 2 == quote.Length || char.IsWhiteSpace(quote[i + 2]));
}
