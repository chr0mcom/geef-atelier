using Geef.Atelier.Web.Services;

namespace Geef.Atelier.Tests.Web.Services;

public sealed class MarkdownRenderingTests
{
    [Fact]
    public void ToMarkupString_Null_ReturnsEmpty()
    {
        var result = MarkdownRenderer.ToMarkupString(null);
        Assert.Equal("", result.Value);
    }

    [Fact]
    public void ToMarkupString_Empty_ReturnsEmpty()
    {
        var result = MarkdownRenderer.ToMarkupString("   ");
        Assert.Equal("", result.Value);
    }

    [Fact]
    public void ToMarkupString_Heading_RendersHTag()
    {
        var result = MarkdownRenderer.ToMarkupString("# Hello World");
        // UseAdvancedExtensions adds id attributes, so check for "<h1" not "<h1>"
        Assert.Contains("<h1", result.Value);
        Assert.Contains("Hello World", result.Value);
    }

    [Fact]
    public void ToMarkupString_Bold_RendersStrongTag()
    {
        var result = MarkdownRenderer.ToMarkupString("**bold text**");
        Assert.Contains("<strong>", result.Value);
        Assert.Contains("bold text", result.Value);
    }

    [Fact]
    public void ToMarkupString_InlineCode_RendersCodeTag()
    {
        var result = MarkdownRenderer.ToMarkupString("`inline code`");
        Assert.Contains("<code>", result.Value);
        Assert.Contains("inline code", result.Value);
    }

    [Fact]
    public void ToMarkupString_Paragraph_RendersPTag()
    {
        var result = MarkdownRenderer.ToMarkupString("Hello paragraph.");
        Assert.Contains("<p>", result.Value);
        Assert.Contains("Hello paragraph.", result.Value);
    }

    [Fact]
    public void ToMarkupString_Link_RendersAnchorTag()
    {
        var result = MarkdownRenderer.ToMarkupString("[link text](https://example.com)");
        Assert.Contains("<a ", result.Value);
        Assert.Contains("https://example.com", result.Value);
        Assert.Contains("link text", result.Value);
    }

    [Fact]
    public void ToMarkupString_RawHtml_IsStripped()
    {
        // DisableHtml() means raw HTML blocks are NOT passed through to output.
        var result = MarkdownRenderer.ToMarkupString("<script>alert('xss')</script> plain text");
        Assert.DoesNotContain("<script>", result.Value);
        Assert.Contains("plain text", result.Value);
    }

    [Fact]
    public void ToMarkupString_InlineHtmlTag_IsStripped()
    {
        var result = MarkdownRenderer.ToMarkupString("text <b>bold</b> more text");
        // DisableHtml strips inline HTML
        Assert.DoesNotContain("<b>", result.Value);
        Assert.Contains("more text", result.Value);
    }

    [Fact]
    public void ToMarkupString_CodeFence_RendersPreCodeBlock()
    {
        var md = "```csharp\nvar x = 1;\n```";
        var result = MarkdownRenderer.ToMarkupString(md);
        Assert.Contains("<pre>", result.Value);
        // UseAdvancedExtensions adds language class, so check for "<code" not "<code>"
        Assert.Contains("<code", result.Value);
    }

    [Fact]
    public void ToMarkupString_UnorderedList_RendersUlLi()
    {
        var result = MarkdownRenderer.ToMarkupString("- item one\n- item two");
        Assert.Contains("<ul>", result.Value);
        Assert.Contains("<li>", result.Value);
        Assert.Contains("item one", result.Value);
    }
}
