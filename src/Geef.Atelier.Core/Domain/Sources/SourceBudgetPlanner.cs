using Geef.Atelier.Core.Configuration;

namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>One file as the budget planner sees it.</summary>
public sealed record SourceBudgetItem(Guid SourceId, string FileName, int CharCount, int? PageCount);

/// <summary>The size class of all files of an order together with the limits it was measured against.</summary>
public sealed record SourceBudgetPlan(SourceBudgetClass Class, int TotalChars, int FullTextMaxChars, int MapMaxChars, IReadOnlyList<SourceBudgetItem> Items);

/// <summary>Measures files against the character budget of <see cref="SourceOptions"/>.</summary>
public static class SourceBudgetPlanner
{
    /// <summary>
    /// Up to <see cref="SourceOptions.FullTextMaxChars"/> is <see cref="SourceBudgetClass.FullText"/>, up to
    /// <see cref="SourceOptions.MapMaxChars"/> is <see cref="SourceBudgetClass.WithMap"/>, above is
    /// <see cref="SourceBudgetClass.TooLarge"/>. Both limits are inclusive.
    /// </summary>
    public static SourceBudgetClass Classify(int chars, SourceOptions options) =>
        throw new NotImplementedException();

    /// <summary>Classifies the sum of all <paramref name="items"/>.</summary>
    public static SourceBudgetPlan Plan(IReadOnlyList<SourceBudgetItem> items, SourceOptions options) =>
        throw new NotImplementedException();
}
