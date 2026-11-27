namespace Geef.Atelier.Web.Display;

/// <summary>Turns a number of characters into a rough number of pages for people who think in pages.</summary>
public static class PageCountExtensions
{
    /// <summary>
    /// About how many pages <paramref name="chars"/> characters fill, at 2,500 characters a page, rounded to tens and never below one,
    /// as the explanation page says it.
    /// </summary>
    public static int ApproxPages(this int chars) => Math.Max(1, (int)Math.Round(chars / 2_500d / 10d) * 10);
}
