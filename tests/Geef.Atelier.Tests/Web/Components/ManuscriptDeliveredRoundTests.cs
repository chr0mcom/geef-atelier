using Bunit;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Web.Components.UI;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>
/// When the stored text comes from an earlier round than the last one (the best-effort text of a run without approval), the manuscript says which
/// round it is instead of "After N iterations"; otherwise it reads as before.
/// </summary>
public sealed class ManuscriptDeliveredRoundTests : TestContext
{
    private static RunEntity Run(int? deliveredIteration) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAt = DateTimeOffset.UtcNow,
        Status = RunStatus.Failed,
        BriefingText = "b",
        ConfigJson = "{}",
        FinalText = "# Title\n\nText.",
        DeliveredIteration = deliveredIteration,
    };

    [Fact]
    public void A_text_from_an_earlier_round_names_that_round()
    {
        var cut = RenderComponent<Manuscript>(p => p.Add(m => m.Run, Run(1)).Add(m => m.IterationCount, 3));

        Assert.Contains("Text from iteration 1 of 3", cut.Markup);
        Assert.Contains("Iteration 1 ·", cut.Markup);
        Assert.DoesNotContain("After 3 iterations", cut.Markup);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(null)]
    public void The_last_round_or_an_older_run_reads_as_before(int? deliveredIteration)
    {
        var cut = RenderComponent<Manuscript>(p => p.Add(m => m.Run, Run(deliveredIteration)).Add(m => m.IterationCount, 3));

        Assert.Contains("After 3 iterations", cut.Markup);
        Assert.Contains("Iteration 3 ·", cut.Markup);
    }
}
