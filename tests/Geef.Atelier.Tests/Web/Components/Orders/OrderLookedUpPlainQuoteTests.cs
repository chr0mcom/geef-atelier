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
/// "Looked up" shows passages without the marks of Markdown (contract stage 5, 4.1 c): only the page changes; the stored research,
/// the entry the web page and MCP read and the passage in MCP keep the wording of the page. A display that would change a figure
/// shows the passage as it is.
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
    [InlineData("- Papier 22 Euro\n- Farben 31 Euro", "Papier 22 Euro Farben 31 Euro")]
    [InlineData("### Beiträge 2027\nDer Beitrag beträgt 35 Euro.", "Beiträge 2027 Der Beitrag beträgt 35 Euro.")]
    [InlineData("Treffpunkt: **[Ort]** um {Uhrzeit} Uhr.", "Treffpunkt: [Ort] um {Uhrzeit} Uhr.")]
    public void A_passage_is_shown_without_marks_and_with_every_word_and_figure(string quote, string shown) =>
        Assert.Equal([shown], Shown(View(quote)));

    [Theory]
    [InlineData("Siehe Fußnote[^1] zur Höhe.")]
    [InlineData("Der Wert &#49;3,90 Euro gilt.")]
    public void A_display_that_would_change_a_figure_shows_the_passage_as_it_is(string quote)
    {
        Assert.Equal(quote, quote.ToShownQuote());
        Assert.Equal([quote], Shown(View(quote)));
    }

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
