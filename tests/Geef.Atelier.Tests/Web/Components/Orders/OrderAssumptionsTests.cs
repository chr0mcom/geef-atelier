using Bunit;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
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
}
