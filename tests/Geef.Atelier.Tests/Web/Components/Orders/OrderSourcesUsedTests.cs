using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Web.Components.UI.Orders;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>"From your files": each file, up to three verified quotes, and the fixed caveat line only when a quote is shown.</summary>
public sealed class OrderSourcesUsedTests : TestContext
{
    private const string Caveat = "Shows the file was read and quoted — not that every statement is backed.";

    private static readonly OrderSourceUsageView Notes = new("notizen.txt", SourceUsageMode.FullText, 2_362, null);
    private static readonly OrderSourceUsageView Offer = new("angebot.pdf", SourceUsageMode.FullTextWithMap, 160_485, "1-40");

    private IRenderedComponent<OrderSourcesUsed> Render(IReadOnlyList<OrderSourceUsageView> sources, IReadOnlyList<OrderSourceQuoteView>? quotes) =>
        RenderComponent<OrderSourcesUsed>(p => p.Add(c => c.Sources, sources).Add(c => c.Quotes, quotes).Add(c => c.Language, "de"));

    [Fact]
    public void WithoutQuotesThereIsNoCaveatLine()
    {
        var cut = Render([Notes], null);

        Assert.Contains("read completely · 2,362 characters", cut.Find("[data-testid='order-source-used']").TextContent);
        Assert.Empty(cut.FindAll("[data-testid='order-sources-caveat']"));
        Assert.DoesNotContain("quoted", cut.Markup);
    }

    [Fact]
    public void WithAQuoteTheCaveatLineIsShownWordForWord()
    {
        var cut = Render([Notes, Offer], [new OrderSourceQuoteView("notizen.txt", HonestResultData.FileQuote)]);

        Assert.Equal(Caveat, cut.Find("[data-testid='order-sources-caveat']").TextContent);
        Assert.Equal(Caveat, OrderSourcesUsed.QuotesCaveat);
        var files = cut.FindAll("[data-testid='order-source-used']");
        var quote = Assert.Single(files[0].QuerySelectorAll("[data-testid='order-source-quote']"));
        Assert.Equal("blockquote", quote.TagName.ToLowerInvariant());
        Assert.Equal(HonestResultData.FileQuote, quote.TextContent);
        Assert.Equal("de", quote.GetAttribute("lang"));
        Assert.Empty(files[1].QuerySelectorAll("[data-testid='order-source-quote']"));
        Assert.Contains("pages 1-40", files[1].TextContent);
    }

    [Fact]
    public void AtMostThreeQuotesPerFileWithoutRepeats()
    {
        var quotes = Enumerable.Range(1, 5).Select(i => new OrderSourceQuoteView("notizen.txt", $"Zitat Nummer {i} aus den Notizen"))
            .Prepend(new OrderSourceQuoteView("notizen.txt", "Zitat Nummer 1 aus den Notizen"))
            .ToList();

        var cut = Render([Notes], quotes);

        Assert.Equal(new[] { "Zitat Nummer 1 aus den Notizen", "Zitat Nummer 2 aus den Notizen", "Zitat Nummer 3 aus den Notizen" },
            cut.FindAll("[data-testid='order-source-quote']").Select(e => e.TextContent));
    }

    [Fact]
    public void AQuoteOfAFileMissingFromTheListStillShowsItsFileName()
    {
        var cut = Render([], [new OrderSourceQuoteView("protokoll.md", "Beschluss vom Mai gilt nicht mehr")]);

        var file = cut.Find("[data-testid='order-source-used']");
        Assert.Contains("protokoll.md", file.TextContent);
        Assert.Empty(file.QuerySelectorAll(".order-source-meta"));
        Assert.NotNull(cut.Find("[data-testid='order-sources-caveat']"));
    }

    [Fact]
    public void NoFilesAndNoQuotesShowNothing() => Assert.Equal(string.Empty, Render([], []).Markup.Trim());

    [Fact]
    public void TheBlockUsesNoInsiderWords() =>
        HonestResultData.AssertPlain(Render([Notes], [new OrderSourceQuoteView("notizen.txt", HonestResultData.FileQuote)]).Nodes);
}
