namespace Geef.Atelier.Core.Domain;

/// <summary>
/// What a finished run delivered, independent of its <see cref="RunStatus"/>. A run that stopped without full
/// convergence keeps <see cref="RunStatus.Failed"/> (resume and materialisation depend on it) but still delivers
/// its best draft; the outcome tells those cases apart. Stored as an integer.
/// <para>
/// The outcome is the "everything is stored" signal: it is written only after the final text and the results of
/// the finishing steps (finalizers) are saved, so a client that waits for it never reads a text that is still
/// being changed. While it is <c>null</c> the run is still working, also when its status already looks terminal.
/// The first recorded outcome wins. Runs finished before Step47 keep <c>null</c>.
/// </para>
/// </summary>
public enum RunOutcome
{
    /// <summary>All reviewers approved; the converged text and the results of the finishing steps were saved.</summary>
    Converged = 0,

    /// <summary>
    /// The run stopped without full convergence and its best available draft was saved. This deliberately includes a
    /// run stopped by a critical reviewer finding (status <see cref="RunStatus.Aborted"/>) that has a text: the text is
    /// delivered, the critical finding stays open and is listed with the findings.
    /// </summary>
    DeliveredWithOpenPoints = 1,

    /// <summary>The run ended without a usable text, or its result could not be saved completely.</summary>
    Failed = 2,

    /// <summary>
    /// The run was cancelled by the user or stopped by the service, also when that happened while the finishing steps
    /// were running.
    /// </summary>
    Aborted = 3,
}
