using System.Globalization;
using System.Text.Json;
using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>One section of a <see cref="WritingPlan"/>: its heading, its word budget and what goes into it.</summary>
/// <param name="Heading">Short name of the section, one line.</param>
/// <param name="Words">Word budget of the section.</param>
/// <param name="Content">What the section says and which files it draws on, one line; may be empty.</param>
internal sealed record WritingPlanSection(string Heading, int Words, string Content);

/// <summary>
/// The outline of "Extra careful" with an outline first (D-087): the sections in the order they appear and the length of the whole text.
/// Made by one call before the writing pass (<see cref="WritingPlanner"/>), handed to that pass only.
/// </summary>
/// <param name="TotalWords">Words of the whole text: the length the request asks for when the code can read it, otherwise the sum of the budgets.</param>
/// <param name="Sections">The sections, at least one and at most <see cref="WritingPlanRules.MaxSections"/>.</param>
internal sealed record WritingPlan(int TotalWords, IReadOnlyList<WritingPlanSection> Sections);

/// <summary>Reading, sizing and limits of a <see cref="WritingPlan"/>.</summary>
internal static class WritingPlanRules
{
    /// <summary>Most sections a plan may have; a longer answer is no usable plan.</summary>
    public const int MaxSections = 20;

    /// <summary>Longest heading kept, in characters.</summary>
    public const int MaxHeadingChars = 160;

    /// <summary>Longest description of a section kept, in characters.</summary>
    public const int MaxContentChars = 600;

    /// <summary>Largest budget of one section; a larger number is no usable plan.</summary>
    public const int MaxSectionWords = 20_000;

    /// <summary>Smallest budget of one section after fitting the plan to the requested length.</summary>
    public const int MinSectionWords = 20;

    /// <summary>Shortest whole text a plan may describe; a shorter sum of budgets (or requested length) is no usable plan.</summary>
    public const int MinTotalWords = 50;

    /// <summary>Longest whole text a plan may describe; a longer sum of budgets is no usable plan.</summary>
    public const int MaxTotalWords = 20_000;

    /// <summary>
    /// The plan in <paramref name="text"/>: the first JSON object in it with a list <c>sections</c> of objects with <c>heading</c>,
    /// <c>words</c> and optionally <c>content</c>. <c>null</c> when there is none, when a section has no heading or a budget that is not a
    /// whole number from 1 to <see cref="MaxSectionWords"/>, or when there are no or more than <see cref="MaxSections"/> sections.
    /// Headings and descriptions are put on one line and cut to their limits. <see cref="WritingPlan.TotalWords"/> is the sum of the budgets;
    /// a sum outside <see cref="MinTotalWords"/> to <see cref="MaxTotalWords"/> is no usable plan.
    /// </summary>
    public static WritingPlan? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var start = text.IndexOf('{', StringComparison.Ordinal);
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;

        try
        {
            using var document = JsonDocument.Parse(text[start..(end + 1)]);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sections", out var list)
                || list.ValueKind != JsonValueKind.Array)
                return null;

            var sections = new List<WritingPlanSection>();
            foreach (var item in list.EnumerateArray())
            {
                if (sections.Count == MaxSections || Section(item) is not { } section)
                    return null;
                sections.Add(section);
            }

            return sections.Count == 0 ? null : new WritingPlan(sections.Sum(s => s.Words), sections).Plausible();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The length in words a plan aims at for <paramref name="length"/>, the length the code read from the request: the middle of a range,
    /// 90 % of an upper limit, 110 % of a lower limit, the stated number otherwise. <c>null</c> without a length or for a length in characters
    /// (the planner then reads the length from the request itself).
    /// </summary>
    public static int? TargetWords(this LengthConstraint? length)
    {
        if (length is not { Unit: LengthUnit.Words })
            return null;
        var words = length.Kind switch
        {
            LengthKind.Range when length is { Min: { } min, Max: { } max } => (min + max) / 2,
            LengthKind.AtMost when length.Max is { } max => max * 9 / 10,
            LengthKind.AtLeast when length.Min is { } min => (min * 11 + 9) / 10,
            _ => length.Min ?? length.Max,
        };
        return words is > 0 ? words : null;
    }

    /// <summary>
    /// <paramref name="plan"/> fitted to <paramref name="targetWords"/>: every budget scaled by the same factor, rounded to tens and at
    /// least <see cref="MinSectionWords"/>, and the whole text set to <paramref name="targetWords"/>. Unchanged without a target.
    /// <c>null</c> when the fitted plan is not <see cref="Plausible"/> (for example a requested length below <see cref="MinTotalWords"/>).
    /// </summary>
    public static WritingPlan? FitTo(this WritingPlan plan, int? targetWords)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (targetWords is not > 0 || plan.Sections.Count == 0)
            return plan;

        var sum = plan.Sections.Sum(s => (long)s.Words);
        var sections = plan.Sections
            .Select(s => s with { Words = Math.Max(MinSectionWords, (int)Math.Round(s.Words * (double)targetWords.Value / sum / 10, MidpointRounding.AwayFromZero) * 10) })
            .ToList();
        return new WritingPlan(targetWords.Value, sections).Plausible();
    }

    /// <summary>
    /// <paramref name="plan"/> when both its whole length and the sum of its budgets lie from <see cref="MinTotalWords"/> to
    /// <see cref="MaxTotalWords"/>; otherwise <c>null</c> (no usable plan).
    /// </summary>
    public static WritingPlan? Plausible(this WritingPlan? plan)
    {
        if (plan is null)
            return null;
        var sum = plan.Sections.Sum(s => (long)s.Words);
        return plan.TotalWords is >= MinTotalWords and <= MaxTotalWords && sum is >= MinTotalWords and <= MaxTotalWords ? plan : null;
    }

    private static WritingPlanSection? Section(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object)
            return null;
        var heading = Line(item, "heading", MaxHeadingChars);
        if (heading.Length == 0 || !item.TryGetProperty("words", out var words) || Budget(words) is not { } budget)
            return null;
        return new WritingPlanSection(heading, budget, Line(item, "content", MaxContentChars));
    }

    private static int? Budget(JsonElement words)
    {
        var value = words.ValueKind switch
        {
            JsonValueKind.Number when words.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(words.GetString()?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) => number,
            _ => (int?)null,
        };
        return value is >= 1 and <= MaxSectionWords ? value : null;
    }

    private static string Line(JsonElement item, string name, int maxChars)
    {
        if (!item.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            return string.Empty;
        var line = string.Join(' ', (value.GetString() ?? string.Empty).Split(['\r', '\n', '\t', '\u2028', '\u2029'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();
        return line.Length <= maxChars ? line : line[..(maxChars - 1)].TrimEnd() + "…";
    }
}
