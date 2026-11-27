using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>The files of an order as they go into the writer's prompt, with the order position of every file.</summary>
internal sealed record OrderSourcePromptSet(IReadOnlyList<PromptSource> Sources, IReadOnlyDictionary<Guid, int> Positions);

/// <summary>
/// Builds the prompt sources of an order and the usage rows of a rendered bundle for the direct path
/// (<see cref="OrderGroundingStep"/>), so that it applies the page selection and counts characters the same way as the order
/// service does before the start.
/// </summary>
internal static class OrderSourcePrompts
{
    /// <summary>
    /// Turns <paramref name="items"/> (in order position) into <see cref="PromptSource"/>s with ids <c>S1…Sn</c>,
    /// leaving out <paramref name="skip"/>. A page selection that cannot be applied any more is reported through
    /// <paramref name="selectionNotApplied"/> and the whole file is used.
    /// </summary>
    public static OrderSourcePromptSet ToPromptSources(
        this IReadOnlyList<OrderSourceItem> items,
        Action<OrderSourceItem> selectionNotApplied,
        OrderSourceItem? skip = null)
    {
        var sources = new List<PromptSource>(items.Count);
        var positions = new Dictionary<Guid, int>();
        foreach (var item in items.OrderBy(i => i.Link.Position))
        {
            if (ReferenceEquals(item, skip))
                continue;
            var (text, pagesIncluded) = SelectPages(item, selectionNotApplied);
            var characters = pagesIncluded is null ? item.Source.CharCount : text.CharCountWithoutPageMarkers();
            sources.Add(new PromptSource(
                item.Source.Id, $"S{sources.Count + 1}", item.Source.FileName, item.Source.Kind,
                item.Source.PageCount, pagesIncluded, text, characters));
            positions[item.Source.Id] = item.Link.Position;
        }
        return new OrderSourcePromptSet(sources, positions);
    }

    /// <summary>
    /// One usage row (target <see cref="OrderGroundingStep.WriterTarget"/>) per file of <paramref name="bundle"/>. The passages looked up on
    /// the web (<see cref="PromptSource.LookedUp"/>) get a row without source, target <see cref="OrderGroundingStep.ResearchTarget"/> and the
    /// position after the files.
    /// </summary>
    public static List<RunSourceUsageEntity> ToUsages(this OrderSourceBundle bundle, Guid runId, IReadOnlyDictionary<Guid, int> positions, DateTimeOffset now)
    {
        var afterFiles = positions.Count == 0 ? 0 : positions.Values.Max() + 1;
        return bundle.Sources.Select(s => new RunSourceUsageEntity
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            SourceId = s.LookedUp ? null : s.SourceId,
            FileName = s.FileName,
            Mode = bundle.Mode,
            Target = s.LookedUp ? OrderGroundingStep.ResearchTarget : OrderGroundingStep.WriterTarget,
            CharsIncluded = s.Characters,
            PagesIncluded = s.PagesIncluded,
            Position = s.LookedUp ? afterFiles : positions[s.SourceId],
            CreatedAt = now,
        }).ToList();
    }

    private static (string Text, string? PagesIncluded) SelectPages(OrderSourceItem item, Action<OrderSourceItem> selectionNotApplied)
    {
        var text = item.Source.ExtractedText ?? string.Empty;
        var selection = item.Link.PageSelection;
        if (string.IsNullOrWhiteSpace(selection) || item.Source.PageCount is not { } pageCount)
            return (text, null);

        if (PageSelection.TryParse(selection, pageCount, out var parsed, out _) && parsed is not null)
            return (parsed.ApplyTo(text), parsed.ToString());

        selectionNotApplied(item);
        return (text, null);
    }
}
