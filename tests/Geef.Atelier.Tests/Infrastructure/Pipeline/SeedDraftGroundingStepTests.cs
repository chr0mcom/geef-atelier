using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Sdk.Context;

namespace Geef.Atelier.Tests.Infrastructure.Pipeline;

public sealed class SeedDraftGroundingStepTests
{
    [Fact]
    public async Task RunAsync_SetsGroundedBriefFromInput()
    {
        var step   = new SeedDraftGroundingStep("previous draft");
        var result = await step.RunAsync("the briefing", CancellationToken.None);

        var brief = result.Context.GetRequired(AtelierContextKeys.GroundedBrief);
        Assert.Equal("the briefing", brief);
    }

    [Fact]
    public async Task RunAsync_SetsSeedDraftFromConstructorArg()
    {
        var step   = new SeedDraftGroundingStep("my seed draft text");
        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.True(result.Context.TryGet(AtelierContextKeys.SeedDraft, out var seedDraft));
        Assert.Equal("my seed draft text", seedDraft);
    }

    [Fact]
    public async Task RunAsync_ReturnsEmptyNotes()
    {
        var step   = new SeedDraftGroundingStep("draft");
        var result = await step.RunAsync("briefing", CancellationToken.None);

        Assert.Empty(result.Notes);
    }
}
