using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Tests.Sources;

public sealed class PageMarkersAndSelectionTests
{
    private const string ThreePages = "[[page 1]]\nOne a\nOne b\n[[page 2]]\n\n[[page 3]]\nThree";

    [Fact]
    public void MarkersSplitAndStrip()
    {
        Assert.Equal("[[page 12]]", PageMarkers.For(12));

        var pages = PageMarkers.SplitPages(ThreePages);
        Assert.Equal([(1, "One a\nOne b"), (2, ""), (3, "Three")], pages);
        Assert.Equal("One a\nOne b\n\nThree", PageMarkers.StripMarkers(ThreePages));
        Assert.Equal([(1, "plain text")], PageMarkers.SplitPages("plain text"));
        Assert.Equal("not [[page 2]] a marker", PageMarkers.StripMarkers("not [[page 2]] a marker"));
    }

    [Theory]
    [InlineData("1-40,55", 80, "1-40,55", 41)]
    [InlineData(" 55 , 1 – 40 ", 80, "1-40,55", 41)]
    [InlineData("3,1-2,4", 10, "1-4", 4)]
    [InlineData("7", 7, "7", 1)]
    public void ParsesAndNormalisesSelections(string text, int pageCount, string canonical, int selectedPages)
    {
        Assert.True(PageSelection.TryParse(text, pageCount, out var selection, out var error), error);
        Assert.Equal(canonical, selection!.ToString());
        Assert.Equal(selectedPages, selection.PageCount);
    }

    [Theory]
    [InlineData("0-3", 10, "Page numbers start at 1.")]
    [InlineData("5-3", 10, "A range goes from a lower to a higher page, for example 3-7.")]
    [InlineData("1-11", 10, "Page 11 does not exist; the file has 10 pages.")]
    [InlineData("a-b", 10, "Use page numbers like 1-40, 55.")]
    [InlineData("1-2-3", 10, "Use page numbers like 1-40, 55.")]
    [InlineData(",", 10, "Use page numbers like 1-40, 55.")]
    public void RejectsBadSelectionsWithPlainMessages(string text, int pageCount, string message)
    {
        Assert.False(PageSelection.TryParse(text, pageCount, out var selection, out var error));
        Assert.Null(selection);
        Assert.Equal(message, error);
    }

    [Fact]
    public void BlankSelectionMeansAllPages()
    {
        Assert.True(PageSelection.TryParse("  ", 3, out var selection, out var error));
        Assert.Null(selection);
        Assert.Null(error);
    }

    [Fact]
    public void ApplyToKeepsSelectedPagesWithTheirMarkers()
    {
        Assert.True(PageSelection.TryParse("1,3", 3, out var selection, out _));
        Assert.Equal("[[page 1]]\nOne a\nOne b\n[[page 3]]\nThree", selection!.ApplyTo(ThreePages));
        Assert.True(selection.Contains(3));
        Assert.False(selection.Contains(2));
    }
}
