using Geef.Atelier.Core.Configuration;

namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>One file as the budget planner sees it.</summary>
public sealed record SourceBudgetItem(Guid SourceId, string FileName, int CharCount, int? PageCount);

/// <summary>The size class of all files of an order together with the limits it was measured against.</summary>
public sealed record SourceBudgetPlan(SourceBudgetClass Class, int TotalChars, int FullTextMaxChars, int MapMaxChars, IReadOnlyList<SourceBudgetItem> Items)
{
    /// <summary>Estimated model tokens of <see cref="TotalChars"/> (see <see cref="SourceOptions.CharsPerToken"/>).</summary>
    public int EstimatedTokens { get; init; }
}

/// <summary>Measures files against the character budget of <see cref="SourceOptions"/>.</summary>
public static class SourceBudgetPlanner
{
    /// <summary>
    /// Up to <see cref="SourceOptions.FullTextMaxChars"/> is <see cref="SourceBudgetClass.FullText"/>, up to
    /// <see cref="SourceOptions.MapMaxChars"/> is <see cref="SourceBudgetClass.WithMap"/>, above is
    /// <see cref="SourceBudgetClass.TooLarge"/>. Both limits are inclusive.
    /// </summary>
    public static SourceBudgetClass Classify(int chars, SourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (chars <= options.FullTextMaxChars)
            return SourceBudgetClass.FullText;
        return chars <= options.MapMaxChars ? SourceBudgetClass.WithMap : SourceBudgetClass.TooLarge;
    }

    /// <summary>Classifies the sum of all <paramref name="items"/>; the sum saturates at <see cref="int.MaxValue"/>.</summary>
    public static SourceBudgetPlan Plan(IReadOnlyList<SourceBudgetItem> items, SourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(options);

        var total = (int)Math.Min(int.MaxValue, items.Sum(i => (long)Math.Max(0, i.CharCount)));
        return new SourceBudgetPlan(Classify(total, options), total, options.FullTextMaxChars, options.MapMaxChars, items)
        {
            EstimatedTokens = EstimateTokens(total, options),
        };
    }

    /// <summary>Estimates the model tokens of <paramref name="chars"/> characters with <see cref="SourceOptions.CharsPerToken"/>, rounded up.</summary>
    public static int EstimateTokens(int chars, SourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var perToken = options.CharsPerToken > 0 ? options.CharsPerToken : 1.9;
        return (int)Math.Min(int.MaxValue, Math.Ceiling(Math.Max(0, chars) / perToken));
    }
}
