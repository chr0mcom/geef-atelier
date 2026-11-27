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

    /// <summary>
    /// The safeguards switched on in <paramref name="options"/> for runs of <paramref name="user"/>: per switch the entry of its overrides
    /// for that user name (D-087), otherwise the switch itself.
    /// </summary>
    public static LoopSafeguards From(ConvergenceOptions options, string? user = null) => new(
        Switch(options.BestDraft, options.BestDraftOverrides, user) == LoopSwitch.On,
        Switch(options.WriterBlockingOnly, options.WriterBlockingOnlyOverrides, user) == LoopSwitch.On,
        Switch(options.StopWithoutProgress, options.StopWithoutProgressOverrides, user) == LoopSwitch.On);

    private static LoopSwitch Switch(LoopSwitch value, Dictionary<string, LoopSwitch>? overrides, string? user) =>
        user is not null && overrides is not null && overrides.TryGetValue(user, out var own) ? own : value;
}
