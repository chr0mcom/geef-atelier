using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

/// <summary>How the fact check of one round ended.</summary>
internal enum FactRoundStatus
{
    /// <summary>The mode was <see cref="FactCheckMode.Off"/>: no call.</summary>
    Off = 0,

    /// <summary>
    /// The check answered; <see cref="FactCheckRound.Open"/> holds what is open in the text of the round: the findings of this check
    /// and those of earlier answered rounds that still apply (<see cref="FactCarryOver"/>).
    /// </summary>
    Checked = 1,

    /// <summary>
    /// Neither the model nor its fallback could complete the check; <see cref="FactCheckRound.Open"/> is empty. The report takes what
    /// is still open from the answered rounds (passages still in the delivered text, source conflicts).
    /// </summary>
    Failed = 2,
}

/// <summary>
/// The fact check of one round: mode and scope used, the findings open in the text of that round (source conflicts included), the
/// calls made, the model that answered last and whether it was the fallback model. <see cref="TextHash"/> identifies the wording the
/// check saw (<see cref="HashOf"/>), so the report can tell whether the delivered text is exactly that wording; <see cref="NotShown"/>
/// counts flagged passages that could not be checked or listed (over a limit of the answer or of the list), those of earlier
/// checks included; <see cref="CheckNotShown"/> only those of the check of this round.
/// </summary>
internal sealed record FactCheckRound(
    int Iteration,
    FactCheckMode Mode,
    FactCheckScope Scope,
    FactRoundStatus Status,
    IReadOnlyList<FactFinding> Open,
    int Calls,
    string? Model,
    bool UsedFallbackModel,
    string? TextHash = null,
    int NotShown = 0,
    int CheckNotShown = 0)
{
    /// <summary>SHA-256 (hex) of <paramref name="text"/> after <see cref="QuoteVerifier.Normalize"/> and trimming, so that line ends and spacing the finalizer changes do not count as a new wording.</summary>
    public static string HashOf(string? text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(QuoteVerifier.Normalize(text ?? string.Empty).Trim())));
}

/// <summary>
/// What the fact check of one run did, round by round. One ledger per run: the reviewer writes it, the report of the delivered
/// round reads it. Safe for the reviewer and the end of the run to use from different threads.
/// </summary>
internal sealed class FactCheckLedger
{
    private readonly Lock _gate = new();
    private readonly SortedDictionary<int, FactCheckRound> _rounds = [];
    private readonly HashSet<string> _hung = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Every recorded round in the order of the iterations.</summary>
    public IReadOnlyList<FactCheckRound> Rounds
    {
        get
        {
            lock (_gate)
                return _rounds.Values.ToList();
        }
    }

    /// <summary>Records <paramref name="round"/>; a second record for the same iteration replaces the first.</summary>
    public void Record(FactCheckRound round)
    {
        ArgumentNullException.ThrowIfNull(round);
        lock (_gate)
            _rounds[round.Iteration] = round;
    }

    /// <summary>The round of <paramref name="iteration"/>, or <c>null</c>.</summary>
    public FactCheckRound? For(int iteration)
    {
        lock (_gate)
            return _rounds.GetValueOrDefault(iteration);
    }

    /// <summary>The latest round before <paramref name="iteration"/> whose check answered for exactly the wording <paramref name="textHash"/>, or <c>null</c>.</summary>
    public FactCheckRound? CheckedWording(string textHash, int iteration)
    {
        lock (_gate)
            return _rounds.Values.LastOrDefault(r => r.Iteration < iteration && r.Status == FactRoundStatus.Checked && r.TextHash == textHash);
    }

    /// <summary>Every round before <paramref name="iteration"/> whose check answered, in the order of the iterations.</summary>
    public IReadOnlyList<FactCheckRound> AnsweredBefore(int iteration)
    {
        lock (_gate)
            return _rounds.Values.Where(r => r.Iteration < iteration && r.Status == FactRoundStatus.Checked).ToList();
    }

    /// <summary>Notes that a call of <paramref name="model"/> ran into its time limit in this run; such a model is not called again in the run.</summary>
    public void MarkHung(string model)
    {
        lock (_gate)
            _hung.Add(model);
    }

    /// <summary>True when a call of <paramref name="model"/> ran into its time limit earlier in this run.</summary>
    public bool HasHung(string model)
    {
        lock (_gate)
            return _hung.Contains(model);
    }
}
