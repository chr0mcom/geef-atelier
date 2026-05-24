namespace Geef.Atelier.Infrastructure.Finalizers;

/// <summary>Paragraph-level LCS diff between two texts.</summary>
internal static class ParagraphDiff
{
    public enum Op { Equal, Delete, Insert }

    public sealed record Chunk(Op Op, string Text);

    public static IReadOnlyList<Chunk> Compute(string original, string transformed)
    {
        var a = SplitParagraphs(original);
        var b = SplitParagraphs(transformed);
        return LcsDiff(a, b);
    }

    private static string[] SplitParagraphs(string text) =>
        text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToArray();

    private static List<Chunk> LcsDiff(string[] a, string[] b)
    {
        var n = a.Length;
        var m = b.Length;
        var lcs = new int[n + 1, m + 1];

        for (var i = n - 1; i >= 0; i--)
        for (var j = m - 1; j >= 0; j--)
            lcs[i, j] = string.Equals(a[i], b[j], StringComparison.Ordinal)
                ? 1 + lcs[i + 1, j + 1]
                : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<Chunk>();
        var x = 0;
        var y = 0;

        while (x < n || y < m)
        {
            if (x < n && y < m && string.Equals(a[x], b[y], StringComparison.Ordinal))
            {
                result.Add(new Chunk(Op.Equal, a[x]));
                x++;
                y++;
            }
            else if (y < m && (x >= n || lcs[x, y + 1] >= lcs[x + 1, y]))
            {
                result.Add(new Chunk(Op.Insert, b[y]));
                y++;
            }
            else
            {
                result.Add(new Chunk(Op.Delete, a[x]));
                x++;
            }
        }

        return result;
    }
}
