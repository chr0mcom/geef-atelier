namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>Page markers in extracted PDF text: one marker per page, on its own line, before the page text.</summary>
public static class PageMarkers
{
    /// <summary>Composite format of a marker; the argument is the 1-based page number.</summary>
    public const string Format = "[[page {0}]]";

    /// <summary>Returns the marker of page <paramref name="page"/>.</summary>
    public static string For(int page) =>
        throw new NotImplementedException();

    /// <summary>Splits extracted text at its markers into pages; text without markers is one page.</summary>
    public static IReadOnlyList<(int Page, string Text)> SplitPages(string extractedText) =>
        throw new NotImplementedException();

    /// <summary>Removes all marker lines from extracted text.</summary>
    public static string StripMarkers(string extractedText) =>
        throw new NotImplementedException();
}
