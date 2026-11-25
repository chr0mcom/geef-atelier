using System.Text.Json;
using Geef.Atelier.Core.Domain.Research;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>Passages read from the answer and the proposals dropped per reason.</summary>
internal sealed record DossierParseResult(IReadOnlyList<ProposedPassage> Passages, IReadOnlyDictionary<ResearchDropReason, int> Dropped);

/// <summary>
/// Reads the <c>pick_passages</c> answer. Drops (and counts) passages with an unknown page or topic and quotes over
/// <see cref="DossierBuilder.MaxQuoteChars"/> characters, which are never shortened; keeps at most <see cref="MaxPassages"/>.
/// </summary>
/// <remarks>
/// An unknown topic counts as <see cref="ResearchDropReason.UnknownPage"/> (there is no reason of its own), a passage beyond
/// <see cref="MaxPassages"/> as <see cref="ResearchDropReason.OverLimit"/>. A page id is matched without regard to case and kept as the
/// page has it. A missing or empty quote is passed on; the builder counts it as too short.
/// </remarks>
internal static class DossierOutputParser
{
    public const int MaxPassages = 40;

    /// <summary>The passages of <paramref name="json"/> for the pages <paramref name="pageIds"/> and the topics 1…<paramref name="topicCount"/>; <c>null</c> for unusable JSON.</summary>
    public static DossierParseResult? Parse(string json, IReadOnlyCollection<string> pageIds, int topicCount)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("passages", out var items)
                || items.ValueKind != JsonValueKind.Array)
                return null;

            var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in pageIds ?? [])
            {
                if (!string.IsNullOrWhiteSpace(id))
                    ids.TryAdd(id.Trim(), id);
            }

            var passages = new List<ProposedPassage>();
            var dropped = new SortedDictionary<ResearchDropReason, int>();
            foreach (var item in items.EnumerateArray())
            {
                var reason = Read(item, ids, topicCount, out var passage);
                if (reason is null && passages.Count >= MaxPassages)
                    reason = ResearchDropReason.OverLimit;
                if (reason is { } drop)
                    dropped[drop] = dropped.GetValueOrDefault(drop) + 1;
                else
                    passages.Add(passage!);
            }

            return new DossierParseResult(passages, dropped);
        }
    }

    private static ResearchDropReason? Read(JsonElement item, Dictionary<string, string> ids, int topicCount, out ProposedPassage? passage)
    {
        passage = null;
        if (item.ValueKind != JsonValueKind.Object)
            return ResearchDropReason.UnknownPage;
        if (!item.TryGetProperty("topic", out var topicValue) || !TryTopic(topicValue, out var topic) || topic < 1 || topic > topicCount)
            return ResearchDropReason.UnknownPage;
        if (!item.TryGetProperty("page", out var pageValue) || pageValue.ValueKind != JsonValueKind.String
            || pageValue.GetString() is not { } pageText || !ids.TryGetValue(pageText.Trim(), out var pageId))
            return ResearchDropReason.UnknownPage;

        var quote = item.TryGetProperty("quote", out var quoteValue) && quoteValue.ValueKind == JsonValueKind.String
            ? quoteValue.GetString() ?? string.Empty
            : string.Empty;
        if (quote.Length > DossierBuilder.MaxQuoteChars)
            return ResearchDropReason.OverlongQuote;

        passage = new ProposedPassage(topic, pageId, quote);
        return null;
    }

    private static bool TryTopic(JsonElement value, out int topic)
    {
        topic = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out topic);
    }
}
