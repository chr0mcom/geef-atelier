using Geef.Atelier.Infrastructure.Orders.Export;
using Geef.Atelier.Infrastructure.Markup;
using Geef.Atelier.Web.Services;
using Markdig;

namespace Geef.Atelier.Tests.Markup;

/// <summary>Texts of a model may carry words of a hostile file or web page; nothing in them may act on its own or disappear.</summary>
public sealed class SafeMarkdownTests
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseSafeExtensions().DisableHtml().Build();

    public static TheoryData<string> Attacks() =>
    [
        "Hello **world**{onmouseover=\"alert(1)\"}",
        "<https://ok.example>{onfocus=alert(1) autofocus=true tabindex=0}",
        "*hi*{style=\"background:url(https://evil.example/p?d=1);display:block\"}",
        "[ok](https://ok.example){ping=\"https://evil.example/p\"}",
        "## Offer {#x onclick=alert(1)}",
        "<script>alert(1)</script>",
        "<img src=x onerror=alert(1)>",
        "[click](javascript:alert(1))",
        "![pixel](https://evil.example/p.png)",
        "![video](https://www.youtube.com/watch?v=abc)",
        "<javascript:alert(1)>",
    ];

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Nothing_in_a_text_acts_on_its_own_in_the_page_or_in_the_html_export(string markdown)
    {
        foreach (var html in new[] { markdown.ToSafeHtml(Pipeline), MarkdownToHtmlConverter.Convert(markdown), MarkdownRenderer.ToHtml(markdown) })
        {
            var body = html.Contains("<body>") ? html[html.IndexOf("<body>", StringComparison.Ordinal)..] : html;
            Assert.DoesNotMatch("<[a-zA-Z][^>]*\\s(on[a-z]+|style|ping|autofocus)\\s*=", body);
            Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<iframe", body, StringComparison.OrdinalIgnoreCase);
        }

        var shown = markdown.ToSafeHtml(Pipeline);
        Assert.DoesNotContain("href=\"javascript", shown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", shown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_placeholder_in_braces_stays_visible_in_every_output()
    {
        const string markdown = "Sehr geehrte Frau {Name}\n\n## Angebot {Entwurf}\n\nBetrag: **{Betrag}**";

        foreach (var output in new[] { markdown.ToSafeHtml(Pipeline), MarkdownToHtmlConverter.Convert(markdown), PlaintextStripper.Strip(markdown) })
        {
            Assert.Contains("{Name}", output);
            Assert.Contains("{Entwurf}", output);
            Assert.Contains("{Betrag}", output);
        }
    }

    [Fact]
    public void Tables_lists_and_web_links_still_work()
    {
        var html = "| a | b |\n|---|---|\n| 1 | 2 |\n\n- [x] done\n\n[site](https://ok.example) <mail@ok.example>".ToSafeHtml(Pipeline);

        Assert.Contains("<table>", html);
        Assert.Contains("href=\"https://ok.example\"", html);
        Assert.Contains("href=\"mailto:mail@ok.example\"", html);
    }
}
