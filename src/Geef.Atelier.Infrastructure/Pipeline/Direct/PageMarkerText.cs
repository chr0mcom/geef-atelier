using System.Text.RegularExpressions;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>Helpers for extracted text with page-marker lines (<c>[[page N]]</c>, one per line).</summary>
internal static partial class PageMarkerText
{
    [GeneratedRegex(@"^\[\[page \d+\]\]\r?$\n?", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerLine();

    /// <summary>Removes the marker lines and trims the result.</summary>
    public static string StripMarkerLines(string text) => MarkerLine().Replace(text, string.Empty).Trim();

    /// <summary>Counts the characters of <paramref name="text"/> without its marker lines.</summary>
    public static int CountWithoutMarkers(string text) => MarkerLine().Replace(text, string.Empty).Trim().Length;
}
