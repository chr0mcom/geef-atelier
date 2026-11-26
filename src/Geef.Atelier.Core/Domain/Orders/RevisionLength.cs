using Geef.Atelier.Core.Domain.Dashboard;

namespace Geef.Atelier.Core.Domain.Orders;

/// <summary>
/// Whether a version that changed an earlier one became clearly longer than the text it changed. Worked out from the stored texts each time
/// it is shown, never stored, so versions written before the rule existed get it as well.
/// </summary>
public static class RevisionLength
{
    /// <summary>A changed version is clearly longer when it has more than this share of the words of the text it changed, in percent.</summary>
    public const int LongerPercent = 115;

    /// <summary>True when <paramref name="newWords"/> is more than <see cref="LongerPercent"/> % of <paramref name="baseWords"/>; never for an empty base.</summary>
    public static bool IsClearlyLonger(int baseWords, int newWords) =>
        baseWords > 0 && newWords * 100L > baseWords * (long)LongerPercent;

    /// <summary>
    /// True when <paramref name="run"/> changed an earlier version on the person's request (<see cref="RunEntity.RevisionHint"/>) and its text
    /// is clearly longer than the text it was given (<see cref="RunEntity.SeedDraftText"/>), both counted like <see cref="RunEntity.WordCount"/>.
    /// False for first versions, for runs without a change request and for runs without a text.
    /// </summary>
    public static bool GrewBeyondItsBase(this RunEntity run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (string.IsNullOrWhiteSpace(run.RevisionHint) || string.IsNullOrWhiteSpace(run.FinalText) || string.IsNullOrWhiteSpace(run.SeedDraftText))
            return false;
        return IsClearlyLonger(WordCounter.Count(run.SeedDraftText), WordCounter.Count(run.FinalText));
    }
}
