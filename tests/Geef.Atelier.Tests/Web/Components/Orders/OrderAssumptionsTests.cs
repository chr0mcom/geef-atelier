using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Web.Components.UI.Orders;

namespace Geef.Atelier.Tests.Web.Components.Orders;

/// <summary>"Assumptions" after the text: what nobody said and was assumed, by the questions before writing and by the writer.</summary>
public sealed class OrderAssumptionsTests : TestContext
{
    private static readonly IntakeAssumption Clarifier = new("a1", GapTopic.Tone, HonestResultData.ClarifierAssumption, AssumptionConfidence.Unsure);

    private static OrderResult Result(IReadOnlyList<IntakeAssumption>? clarifier, IReadOnlyList<string>? writer) =>
        OrderTestData.Result(OrderTestData.View(OrderDisplayState.Delivered)) with { Assumptions = clarifier, WriterAssumptions = writer };

    private IRenderedComponent<OrderAssumptions> Render(OrderResult result, string? language = "de") =>
        RenderComponent<OrderAssumptions>(p => p.Add(c => c.Result, result).Add(c => c.Language, language));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NothingIsShownWithoutAssumptions(bool emptyLists)
    {
        var cut = Render(emptyLists ? Result([], []) : Result(null, null));

        Assert.Empty(cut.FindAll("[data-testid='order-assumptions']"));
        Assert.Equal(string.Empty, cut.Markup.Trim());
    }

    [Fact]
    public void OnlyTheQuestionsBeforeWriting()
    {
        var cut = Render(Result([Clarifier], null));

        Assert.Equal("Assumptions", cut.Find("[data-testid='order-assumptions'] h2").TextContent);
        Assert.Contains("You did not say this, so it was assumed:", cut.Markup);
        Assert.Equal(new[] { HonestResultData.ClarifierAssumption }, cut.FindAll("[data-testid='order-assumption']").Select(e => e.TextContent));
    }

    [Fact]
    public void OnlyTheWriter()
    {
        var cut = Render(Result(null, [HonestResultData.WriterAssumption]));

        Assert.Equal(new[] { HonestResultData.WriterAssumption }, cut.FindAll("[data-testid='order-assumption']").Select(e => e.TextContent));
    }

    [Fact]
    public void BothInOneListClarifierFirst()
    {
        var cut = Render(Result([Clarifier], [HonestResultData.WriterAssumption, " "]));

        Assert.Equal(new[] { HonestResultData.ClarifierAssumption, HonestResultData.WriterAssumption },
            cut.FindAll("[data-testid='order-assumption']").Select(e => e.TextContent));
    }

    [Fact]
    public void EachAssumptionCarriesTheLanguageOfTheOrder()
    {
        var cut = Render(Result([Clarifier], [HonestResultData.WriterAssumption]), "de");

        Assert.All(cut.FindAll("[data-testid='order-assumption']"), e => Assert.Equal("de", e.GetAttribute("lang")));
        var unknown = Render(Result([Clarifier], null), null);
        Assert.False(unknown.Find("[data-testid='order-assumption']").HasAttribute("lang"));
    }

    [Fact]
    public void TheBlockUsesNoInsiderWords() =>
        HonestResultData.AssertPlain(Render(Result([Clarifier], [HonestResultData.WriterAssumption])).Nodes);

    [Fact]
    public void FactsThatWereNotGivenAreListedApartAndNeverAsAssumed()
    {
        var photos = new IntakeAssumption("a9", GapTopic.Facts, "Fotos des Schadens werden als Anlage erwähnt", AssumptionConfidence.Sure);
        var cut = Render(Result([Clarifier, photos], null) with { NotGivenFacts = ["Fotos des Schadens werden als Anlage erwähnt"] });

        Assert.Equal(new[] { HonestResultData.ClarifierAssumption }, cut.FindAll("[data-testid='order-assumption']").Select(e => e.TextContent));
        Assert.Equal(new[] { "Fotos des Schadens werden als Anlage erwähnt" }, cut.FindAll("[data-testid='order-not-given']").Select(e => e.TextContent));
        Assert.Contains("You did not give these facts, so they were left open instead of assumed:", cut.Markup);
        HonestResultData.AssertPlain(cut.Nodes);
    }

    [Fact]
    public void WhenTheTextUsedPassagesLookedUpTheFactsNotGivenSayTheyMayBeCovered()
    {
        var photos = new IntakeAssumption("a9", GapTopic.Facts, "Der Mindestlohn im nächsten Jahr", AssumptionConfidence.Sure);
        var result = Result([Clarifier, photos], null) with { NotGivenFacts = ["Der Mindestlohn im nächsten Jahr"], LookedUp = HonestResultData.LookedUp() };

        var cut = Render(result);

        Assert.Equal("You did not give these facts, so they were not assumed. Where the pages looked up cover one, the text may use it:",
            cut.Find("[data-testid='order-not-given-lead']").TextContent);
        Assert.DoesNotContain("left open instead of assumed", cut.Markup, StringComparison.Ordinal);
        HonestResultData.AssertPlain(cut.Nodes);
    }

    [Theory]
    [InlineData(ResearchStatus.NothingFound)]
    [InlineData(ResearchStatus.Failed)]
    [InlineData(ResearchStatus.AllWithheld)]
    public void WithoutPassagesLookedUpInTheTextTheFactsNotGivenKeepTheirLine(ResearchStatus status)
    {
        var photos = new IntakeAssumption("a9", GapTopic.Facts, "Der Mindestlohn im nächsten Jahr", AssumptionConfidence.Sure);
        var result = Result([photos], null) with
        {
            NotGivenFacts = ["Der Mindestlohn im nächsten Jahr"],
            LookedUp = new OrderLookedUpView(status, "x", 0, []),
        };

        Assert.Equal(OrderAssumptions.NotGivenLeadWithoutLookedUp, Render(result).Find("[data-testid='order-not-given-lead']").TextContent);
    }

    [Fact]
    public void AWriterAssumptionThatOnlyAddsAnArticleIsShownOnceAndOtherWordingsStay()
    {
        var clarifier = new IntakeAssumption("a1", GapTopic.Formality, "Eltern werden mit Sie angesprochen.", AssumptionConfidence.Sure);
        var tone = new IntakeAssumption("a2", GapTopic.Tone, "Freundlich, herzlich, aber klar und sachlich.", AssumptionConfidence.Sure);

        var cut = Render(Result([clarifier, tone], ["Die Eltern werden mit Sie angesprochen.", "Der Ton ist freundlich und herzlich, aber sachlich gehalten."]));

        Assert.Equal(
            new[] { "Eltern werden mit Sie angesprochen.", "Freundlich, herzlich, aber klar und sachlich.", "Der Ton ist freundlich und herzlich, aber sachlich gehalten." },
            cut.FindAll("[data-testid='order-assumption']").Select(e => e.TextContent));
    }
}
