using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Persistence.Sources;

namespace Geef.Atelier.Application.Intake;

/// <summary>
/// What the clarifier sees of the files: text without page markers after the page selection, per file the whole text or the
/// first <see cref="IntakeOptions.ExcerptHeadChars"/> and last <see cref="IntakeOptions.ExcerptTailChars"/> characters (address
/// and signature sit there), at most <see cref="MaxFilesWithExcerpt"/> files with an excerpt and
/// <see cref="IntakeOptions.ExcerptTotalChars"/> characters together. A file that does not fit any more is listed by name only.
/// </summary>
internal static class IntakeExcerpts
{
    /// <summary>Most files that get an excerpt.</summary>
    public const int MaxFilesWithExcerpt = 5;

    /// <summary>Line between the start and the end of a shortened excerpt.</summary>
    public const string Gap = "\n[…]\n";

    /// <summary>The files of an order in their order position, numbered from 1.</summary>
    public static IReadOnlyList<IntakeFile> Build(IReadOnlyList<OrderSourceItem> items, IntakeOptions options)
    {
        var files = new List<IntakeFile>(items.Count);
        var remaining = Math.Max(0, options.ExcerptTotalChars);
        var head = Math.Max(0, options.ExcerptHeadChars);
        var tail = Math.Max(0, options.ExcerptTailChars);
        var withExcerpt = 0;

        foreach (var item in items.OrderBy(i => i.Link.Position))
        {
            var source = item.Source;
            var text = Text(item);
            var excerpt = string.Empty;
            var truncated = false;

            if (withExcerpt < MaxFilesWithExcerpt && text.Length > 0)
            {
                if (text.Length <= head + tail && text.Length <= remaining)
                {
                    excerpt = text;
                }
                else if (text.Length > head + tail && head + tail > 0 && head + tail + Gap.Length <= remaining)
                {
                    excerpt = $"{text[..head].TrimEnd()}{Gap}{text[^tail..].TrimStart()}";
                    truncated = true;
                }
            }

            if (excerpt.Length > 0)
            {
                withExcerpt++;
                remaining -= excerpt.Length;
            }

            files.Add(new IntakeFile(files.Count + 1, source.Id, source.FileName, source.Kind, source.PageCount,
                source.CharCount, source.WordCount, excerpt, truncated));
        }

        return files;
    }

    private static string Text(OrderSourceItem item)
    {
        var text = item.Source.ExtractedText ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(item.Link.PageSelection) && item.Source.PageCount is { } pages
            && PageSelection.TryParse(item.Link.PageSelection, pages, out var selection, out _) && selection is not null)
            text = selection.ApplyTo(text);
        return text.WithoutPageMarkers();
    }
}
