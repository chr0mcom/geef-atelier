using Geef.Atelier.Core.Domain.Research;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>Passages read from the answer and the proposals dropped per reason.</summary>
internal sealed record DossierParseResult(IReadOnlyList<ProposedPassage> Passages, IReadOnlyDictionary<ResearchDropReason, int> Dropped);

/// <summary>
/// Reads the <c>pick_passages</c> answer. Drops (and counts) passages with an unknown page or topic and quotes over
/// <see cref="DossierBuilder.MaxQuoteChars"/> characters, which are never shortened; keeps at most <see cref="MaxPassages"/>.
/// </summary>
internal static class DossierOutputParser
{
    public const int MaxPassages = 40;

    /// <summary>The passages of <paramref name="json"/> for the pages <paramref name="pageIds"/> and the topics 1…<paramref name="topicCount"/>; <c>null</c> for unusable JSON.</summary>
    public static DossierParseResult? Parse(string json, IReadOnlyCollection<string> pageIds, int topicCount) => throw new NotImplementedException();
}
