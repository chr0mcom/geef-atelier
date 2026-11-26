using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;

namespace Geef.Atelier.Application.Runs;

/// <summary>When "Resume" is refused for a run that changed an earlier version of its order, the same on the run page and in the service.</summary>
public static class ResumeRules
{
    /// <summary>True for a run of an order that changes an earlier version (<see cref="RunEntity.RevisionHint"/> not blank).</summary>
    public static bool ChangesAnEarlierVersion(this RunEntity run) =>
        run is { Kind: RunKind.Standard, OrderId: not null } && !string.IsNullOrWhiteSpace(run.RevisionHint);

    /// <summary>
    /// The sentence that refuses "Resume" for <paramref name="run"/> without looking at the other versions of its order:
    /// <see cref="RevisionLines.ResumeChangedText"/> when the run changed an earlier version and already has its own text (resuming it would
    /// make the same change again to a text that has it); <c>null</c> otherwise.
    /// </summary>
    public static string? ResumeRefusal(this RunEntity run) =>
        run.ChangesAnEarlierVersion() && !string.IsNullOrWhiteSpace(run.FinalText) ? RevisionLines.ResumeChangedText : null;
}
