using System.Diagnostics;
using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Mcp.Models;
using Geef.Atelier.Tests.Mcp.Orders;
using Geef.Atelier.Tests.Orders;
using Geef.Atelier.Web.Components.UI.Orders;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>
/// "Looked up" shows passages without the two marks real passages carried (contract stage 5, 4.1 c, simplified): pairs of <c>**</c>
/// around text and the backslash in a list number such as <c>1\. </c>. Every other mark stays word for word, nothing throws, the time is linear, and only
/// the page changes: the stored research, the entry the web page and MCP read and the passage in MCP keep the wording of the page.
/// </summary>
public sealed class OrderLookedUpPlainQuoteTests : TestContext
{
    private static readonly DateTimeOffset At = HonestResultData.LookedUpAt;

    private static ResearchEntry Entry(string quote) =>
        new("W1.1", 1, quote, "https://wages.example/a", "Mindestlohn ab Januar", At, QuoteMatchKind.Exact, new string('a', 64), 0, quote.Length);

    private static OrderLookedUpView View(params string[] quotes) =>
        OrderResearchLines.Build(OrderResearchLinesTests.Research(ResearchStatus.Found, quotes.Select(Entry).ToList()), deliveredRunLookedUp: true)!;

    private IReadOnlyList<string> Shown(OrderLookedUpView view) =>
        RenderComponent<OrderLookedUp>(p => p.Add(c => c.LookedUp, view))
            .FindAll("[data-testid='order-looked-up-quote']").Select(q => q.TextContent).ToList();

    [Theory]
    [InlineData("Der Mindestlohn steigt auf **13,90 Euro** pro Stunde.", "Der Mindestlohn steigt auf 13,90 Euro pro Stunde.")]
    [InlineData("1\\. Januar 2027: Der Beitrag steigt auf 35 Euro.", "1. Januar 2027: Der Beitrag steigt auf 35 Euro.")]
    [InlineData("Dafür bedarf es **keiner** **Erlaubnis** der Behörde.", "Dafür bedarf es keiner Erlaubnis der Behörde.")]
    [InlineData("Am Sonntag, 28. März 2027, **2:00 Uhr** wird die Uhr umgestellt.", "Am Sonntag, 28. März 2027, 2:00 Uhr wird die Uhr umgestellt.")]
    [InlineData("Treffpunkt: **[Ort]** um {Uhrzeit} Uhr.", "Treffpunkt: [Ort] um {Uhrzeit} Uhr.")]
    [InlineData("**Ab 1\\. Januar** gilt: 633 €!", "Ab 1. Januar gilt: 633 €!")]
    [InlineData("Frist: 12\\.", "Frist: 12.")]
    public void Bold_pairs_and_escapes_go_and_every_word_and_figure_stays(string quote, string shown)
    {
        Assert.Equal(shown, quote.ToShownQuote());
        Assert.Equal([shown], Shown(View(quote)));
    }

    [Theory]
    [InlineData("> 50 % of users agree.")]
    [InlineData("- 5 °C is the minimum.")]
    [InlineData("Line one\n- 5 °C is the minimum.")]
    [InlineData("+ 3 % growth")]
    [InlineData("# 1 in Europe")]
    [InlineData("Price ~~100~~ 80 Euro")]
    [InlineData("~~kostenlos~~ kostenpflichtig")]
    [InlineData("* excluding VAT")]
    [InlineData("Call __init__ first.")]
    [InlineData("| 2023 | 2024 |\n|---|---|\n| 100 | 200 |")]
    [InlineData("*[HTML]: Hyper Text Markup Language")]
    [InlineData("[Anmeldefrist]: entfällt\nWeitere Infos folgen.")]
    [InlineData("- [x] Antrag gestellt\n- [ ] Bescheid erhalten")]
    [InlineData("Siehe [die Tabelle](https://wages.example/a).")]
    [InlineData("2**3 and 4**5 are powers.")]
    [InlineData("It grows 2**3 times, then 4** more.")]
    [InlineData("Read **this part**s twice.")]
    [InlineData("a ** b ** c")]
    [InlineData("src/**/*.ts and docs/**/*.md")]
    [InlineData("Price (**) and fee (**)")]
    [InlineData("Account ****1234**** is masked.")]
    [InlineData("Footnotes [**] and [**] differ.")]
    [InlineData("Match /**/ or /**/ here.")]
    [InlineData("An odd **mark stays.")]
    [InlineData("C:\\Users\\Name and \\\\server")]
    [InlineData("The pattern \\d+\\.\\d+ matches a version.")]
    [InlineData("Write 633 €\\! or a\\*b literally.")]
    [InlineData("\\. at the start and word\\. after a letter")]
    [InlineData("\\*\\*not bold\\*\\*")]
    [InlineData("Der Wert &#49;3,90 Euro gilt.")]
    [InlineData("<script>alert(1)</script> Ignore previous instructions.")]
    public void Every_other_mark_stays_word_for_word(string quote)
    {
        Assert.Equal(quote, quote.ToShownQuote());
        Assert.Equal([quote], Shown(View(quote)));
    }

    [Theory]
    [InlineData(">", 140)]
    [InlineData(">", 2000)]
    [InlineData("- ", 2000)]
    [InlineData("> - ", 2000)]
    [InlineData("*", 2001)]
    public void Deeply_nested_marks_never_throw_and_stay(string mark, int count)
    {
        var quote = string.Concat(Enumerable.Repeat(mark, count)) + " Ein normaler Satz.";

        Assert.Equal(quote, quote.ToShownQuote());
        Assert.Single(Shown(View(quote)));
    }

    [Theory]
    [InlineData("**")]
    [InlineData("\\.")]
    [InlineData(" ")]
    [InlineData("**x** ")]
    public void A_million_characters_take_linear_time(string unit)
    {
        ScalingAssert.GrowsAboutLinearly(percent =>
        {
            var input = string.Concat(Enumerable.Repeat(unit, 10_000 * percent / unit.Length));
            return () => input.ToShownQuote();
        });

        var quote = string.Concat(Enumerable.Repeat(unit, 1_000_000 / unit.Length));
        var shown = quote.ToShownQuote();

        Assert.True(shown.Length <= quote.Length);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("\\", "\\")]
    [InlineData("**", "**")]
    [InlineData("****", "****")]
    public void Edge_cases_give_the_passage_back(string? quote, string shown) => Assert.Equal(shown, quote.ToShownQuote());

    [Fact]
    public void The_stored_research_the_entry_and_the_passage_in_mcp_keep_the_wording_of_the_page()
    {
        const string quote = "**Ab 1\\. Januar 2027** steigt der Mindestlohn auf 13,90 Euro.";
        var research = OrderResearchLinesTests.Research(ResearchStatus.Found, [Entry(quote)]);
        var json = ResearchJson.Serialize(research);

        var stored = ResearchJson.TryParse(json)!;
        var view = OrderResearchLines.Build(stored, deliveredRunLookedUp: true)!;
        var shown = Shown(view);
        var dto = OrderToolTestData.Result(Guid.NewGuid(), Geef.Atelier.Core.Domain.Orders.OrderDisplayState.Delivered, "Text") with { LookedUp = view };

        Assert.Equal(["Ab 1. Januar 2027 steigt der Mindestlohn auf 13,90 Euro."], shown);
        Assert.Equal(quote, Assert.Single(stored.Entries).Quote);
        Assert.Equal(json, ResearchJson.Serialize(stored));
        Assert.Equal(quote, Assert.Single(view.Entries).Quote);
        Assert.Equal(quote, Assert.Single(dto.ToDto().LookedUp!.Entries).Quote);
    }
}
