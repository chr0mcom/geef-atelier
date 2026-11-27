using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Application.Runs;

/// <summary>When "Resume" is refused, the same on the run page and in the service.</summary>
public static class ResumeRules
{
    /// <summary>
    /// The sentence for a run that was set up for a way of working that no longer exists (the old review loop, a composed or a learning
    /// run, D-091): only runs of the direct path can be continued.
    /// </summary>
    public const string RetiredSetupMessage =
        "This task was set up for a way of working that no longer exists, so it cannot be continued. Please start it again.";

    /// <summary>True for a run of an order that changes an earlier version (<see cref="RunEntity.RevisionHint"/> not blank).</summary>
    public static bool ChangesAnEarlierVersion(this RunEntity run) =>
        run is { Kind: RunKind.Standard, OrderId: not null } && !string.IsNullOrWhiteSpace(run.RevisionHint);

    /// <summary>
    /// The sentence that refuses "Resume" for <paramref name="run"/> without looking at the other versions of its order:
    /// <see cref="RetiredSetupMessage"/> for a run that is not of the direct path (<see cref="RunEntity.CrewTemplateName"/>);
    /// <see cref="RevisionLines.ResumeChangedText"/> when the run changed an earlier version and already has its own text (resuming it would
    /// make the same change again to a text that has it); <c>null</c> otherwise.
    /// </summary>
    public static string? ResumeRefusal(this RunEntity run) =>
        !SystemCrew.IsDirectPathTemplate(run.CrewTemplateName) || run.Kind != RunKind.Standard ? RetiredSetupMessage
        : run.ChangesAnEarlierVersion() && !string.IsNullOrWhiteSpace(run.FinalText) ? RevisionLines.ResumeChangedText
        : null;
}
