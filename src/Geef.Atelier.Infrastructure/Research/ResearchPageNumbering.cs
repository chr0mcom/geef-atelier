using Geef.Atelier.Core.Domain.Research;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>The one numbering of the pages the passage picker sees; the research service and the measurement tool both use it.</summary>
internal static class ResearchPageNumbering
{
    /// <summary>
    /// Pages <c>P1…</c> in the order of the topics and, per topic, of the results. Results without raw text are left out, and an
    /// address found again (by the same or a later search) keeps its first page.
    /// </summary>
    public static IReadOnlyList<ResearchPage> ToResearchPages(this IReadOnlyList<IReadOnlyList<TavilyHit>> hitsPerTopic)
    {
        var pages = new List<ResearchPage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var topic = 0; topic < hitsPerTopic.Count; topic++)
        {
            foreach (var hit in hitsPerTopic[topic])
            {
                if (string.IsNullOrWhiteSpace(hit.RawContent) || !seen.Add(hit.Url))
                    continue;
                pages.Add(new ResearchPage($"P{pages.Count + 1}", topic + 1, hit.Url, hit.Title, hit.RawContent));
            }
        }
        return pages;
    }
}
