using Bunit;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// When the stored text comes from an earlier round than the last one (the best-effort text of a run without approval), the manuscript says which
/// round it is instead of "After N rounds"; otherwise it reads as before.
/// </summary>
public sealed class ManuscriptDeliveredRoundTests : TestContext
{
    private static RunEntity Run(int? deliveredIteration) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        Status = RunStatus.Failed,
        BriefingText = "b",
        FinalText = "# Title\n\nText.",
        DeliveredIteration = deliveredIteration,
    };

    [Fact]
    public void A_text_from_an_earlier_round_names_that_round()
    {
        var cut = RenderComponent<Manuscript>(p => p.Add(m => m.Run, Run(1)).Add(m => m.IterationCount, 3));

        Assert.Contains("Text from round 1 of 3", cut.Markup);
        Assert.Contains("Round 1 ·", cut.Markup);
        Assert.DoesNotContain("After 3 rounds", cut.Markup);
    }

    [Fact]
    public void The_manuscript_offers_copying_only_and_says_round()
    {
        var cut = RenderComponent<Manuscript>(p => p.Add(m => m.Run, Run(null)).Add(m => m.IterationCount, 1));

        Assert.Equal("Copy text", Assert.Single(cut.FindAll("button")).TextContent.Trim());
        Assert.DoesNotContain("Export", cut.Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("iteration", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(null)]
    public void The_last_round_or_an_older_run_reads_as_before(int? deliveredIteration)
    {
        var cut = RenderComponent<Manuscript>(p => p.Add(m => m.Run, Run(deliveredIteration)).Add(m => m.IterationCount, 3));

        Assert.Contains("After 3 rounds", cut.Markup);
        Assert.Contains("Round 3 ·", cut.Markup);
    }
}
