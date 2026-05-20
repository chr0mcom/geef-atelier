using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Tests.Domain.Grounding;

public sealed class RefinementOutcomeTests
{
    private static SourceCitation MakeCitation(string title = "Source A") =>
        new(Title: title, Url: "https://example.com", Snippet: "snippet", DocumentReference: null, RelevanceScore: 0.9);

    [Fact]
    public void WhenSkipped_WasSkippedTrue()
    {
        var citations = new[] { MakeCitation() };
        var outcome = new RefinementOutcome(
            RefinedCitations: citations,
            DroppedCitations: [],
            SynthesizedText: null,
            WasSkipped: true,
            SkipReason: "No refinement configured.");

        Assert.True(outcome.WasSkipped);
        Assert.Equal("No refinement configured.", outcome.SkipReason);
        Assert.Empty(outcome.DroppedCitations);
        Assert.Null(outcome.SynthesizedText);
        Assert.Single(outcome.RefinedCitations);
    }

    [Fact]
    public void WhenNotSkipped_WasSkippedFalse_DroppedAndRefinedFilled()
    {
        var kept = MakeCitation("Kept Source");
        var dropped = MakeCitation("Dropped Source");

        var outcome = new RefinementOutcome(
            RefinedCitations: [kept],
            DroppedCitations: [new DroppedCitation(dropped, "Irrelevant to topic.")],
            SynthesizedText: null,
            WasSkipped: false,
            SkipReason: null);

        Assert.False(outcome.WasSkipped);
        Assert.Null(outcome.SkipReason);
        Assert.Single(outcome.RefinedCitations);
        Assert.Equal("Kept Source", outcome.RefinedCitations[0].Title);
        Assert.Single(outcome.DroppedCitations);
        Assert.Equal("Dropped Source", outcome.DroppedCitations[0].Original.Title);
        Assert.Equal("Irrelevant to topic.", outcome.DroppedCitations[0].Reason);
    }
}
