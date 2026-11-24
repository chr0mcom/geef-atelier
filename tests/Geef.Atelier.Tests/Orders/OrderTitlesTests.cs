using Geef.Atelier.Application.Orders;

namespace Geef.Atelier.Tests.Orders;

public sealed class OrderTitlesTests
{
    [Fact]
    public void The_title_is_the_first_line_shortened_at_a_word()
    {
        var title = ("Write a friendly reply to the customer who asked about the delivery date of the new chairs and tables\nMore").ToOrderTitle();

        Assert.Equal("Write a friendly reply to the customer who asked about the delivery date of the…", title);
    }

    [Fact]
    public void A_cut_never_splits_an_emoji()
    {
        var text = new string('x', 78) + "😀" + " tail";

        var title = text.ToOrderTitle();

        Assert.DoesNotContain(title, char.IsSurrogate);
        Assert.Equal(new string('x', 78) + "…", title);
    }

    [Theory]
    [InlineData("", "Untitled")]
    [InlineData("  \n  ", "Untitled")]
    public void An_empty_request_is_untitled(string text, string expected) => Assert.Equal(expected, text.ToOrderTitle());

    [Fact]
    public void File_slugs_keep_letters_and_digits()
    {
        Assert.Equal("angebot-für-müller-2026", "Angebot für Müller, 2026!".ToFileSlug());
        Assert.Equal("text", "!!!".ToFileSlug());
    }
}
