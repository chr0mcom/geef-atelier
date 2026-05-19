namespace Geef.Atelier.Application.Runs;

/// <summary>
/// Parameters for resuming a previously aborted or failed run.
/// </summary>
/// <param name="ParentRunId">The ID of the run to resume.</param>
/// <param name="UseSeedDraft">
/// True: inject the last iteration's ArtifactText as seed draft.
/// False: start fresh with the same briefing (clean retry).
/// </param>
/// <param name="MaxIterationsOverride">
/// When non-null, overrides the convergence policy's MaxIterations for the resumed run.
/// </param>
public sealed record ResumeOptions(
    Guid ParentRunId,
    bool UseSeedDraft,
    int? MaxIterationsOverride
);
