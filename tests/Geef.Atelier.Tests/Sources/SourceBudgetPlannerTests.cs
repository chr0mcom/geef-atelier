using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Tests.Sources;

public sealed class SourceBudgetPlannerTests
{
    [Theory]
    [InlineData(0, SourceBudgetClass.FullText)]
    [InlineData(150_000, SourceBudgetClass.FullText)]
    [InlineData(150_001, SourceBudgetClass.WithMap)]
    [InlineData(600_000, SourceBudgetClass.WithMap)]
    [InlineData(600_001, SourceBudgetClass.TooLarge)]
    public void DefaultLimitsAreInclusive(int chars, SourceBudgetClass expected) =>
        Assert.Equal(expected, SourceBudgetPlanner.Classify(chars, new SourceOptions()));

    [Theory]
    [InlineData(300_000, SourceBudgetClass.FullText)]
    [InlineData(300_001, SourceBudgetClass.WithMap)]
    [InlineData(900_000, SourceBudgetClass.WithMap)]
    [InlineData(900_001, SourceBudgetClass.TooLarge)]
    public void LimitsComeFromTheOptions(int chars, SourceBudgetClass expected) =>
        Assert.Equal(expected, SourceBudgetPlanner.Classify(chars, new SourceOptions { FullTextMaxChars = 300_000, MapMaxChars = 900_000 }));

    [Fact]
    public void PlanSumsAllFilesAndEstimatesTokens()
    {
        var options = new SourceOptions();
        var plan = SourceBudgetPlanner.Plan(
        [
            new SourceBudgetItem(Guid.NewGuid(), "a.pdf", 100_000, 40),
            new SourceBudgetItem(Guid.NewGuid(), "b.md", 50_001, null),
        ], options);

        Assert.Equal(SourceBudgetClass.WithMap, plan.Class);
        Assert.Equal(150_001, plan.TotalChars);
        Assert.Equal(150_000, plan.FullTextMaxChars);
        Assert.Equal(600_000, plan.MapMaxChars);
        Assert.Equal(78_948, plan.EstimatedTokens);
        Assert.Equal(1_000, SourceBudgetPlanner.EstimateTokens(1_900, options));
    }

    [Fact]
    public void PlanSaturatesInsteadOfOverflowing()
    {
        var plan = SourceBudgetPlanner.Plan(
        [
            new SourceBudgetItem(Guid.NewGuid(), "a", int.MaxValue, null),
            new SourceBudgetItem(Guid.NewGuid(), "b", int.MaxValue, null),
        ], new SourceOptions());
        Assert.Equal(int.MaxValue, plan.TotalChars);
        Assert.Equal(SourceBudgetClass.TooLarge, plan.Class);
    }
}
