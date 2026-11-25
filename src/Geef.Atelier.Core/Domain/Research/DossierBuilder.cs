namespace Geef.Atelier.Core.Domain.Research;

/// <summary>
/// Turns proposed passages into entries without an AI model: every passage must stand word for word in the raw text of its page, and the
/// entry keeps the wording of the page, not the proposal. Each dropped passage is counted with its <see cref="ResearchDropReason"/>.
/// </summary>
public static class DossierBuilder
{
    public const int MinWords = 5;
    public const int MaxQuoteChars = 600;
    public const int MaxPerPage = 3;
    public const int MaxPerTopic = 4;

    /// <summary>Builds the entries <c>W1.1…</c> in the order of <paramref name="proposed"/>.</summary>
    public static (IReadOnlyList<ResearchEntry> Entries, IReadOnlyDictionary<ResearchDropReason, int> Dropped) Build(
        IReadOnlyList<ProposedPassage> proposed, IReadOnlyList<ResearchPage> pages, string requestText, int maxEntries, DateTimeOffset retrievedAt) =>
        throw new NotImplementedException();
}
