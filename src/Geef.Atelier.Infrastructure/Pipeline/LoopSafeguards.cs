using Geef.Atelier.Infrastructure.Configuration;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// The review-loop safeguards of one run (D-085), read from <see cref="ConvergenceOptions"/>. Only the standard and the resumed
/// review loop take them; with every switch off the loop is built exactly as before.
/// </summary>
/// <param name="BestDraft">Deliver the best round instead of the round the SDK picks (<see cref="BestDraftFinalizer"/>).</param>
/// <param name="WriterBlockingOnly">Hand the writer only the blocking findings of the last round.</param>
/// <param name="StopWithoutProgress">Stop after two rounds in a row without progress (<see cref="NoProgressStopPolicy"/>).</param>
internal sealed record LoopSafeguards(bool BestDraft, bool WriterBlockingOnly, bool StopWithoutProgress)
{
    /// <summary>All safeguards off: the loop as it was before D-085.</summary>
    public static LoopSafeguards None { get; } = new(false, false, false);

    /// <summary>The safeguards switched on in <paramref name="options"/>.</summary>
    public static LoopSafeguards From(ConvergenceOptions options) => new(
        options.BestDraft == LoopSwitch.On,
        options.WriterBlockingOnly == LoopSwitch.On,
        options.StopWithoutProgress == LoopSwitch.On);
}
