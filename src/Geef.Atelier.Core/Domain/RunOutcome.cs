namespace Geef.Atelier.Core.Domain;

/// <summary>
/// What a finished run delivered, independent of its <see cref="RunStatus"/>. A run that stopped without full
/// convergence keeps <see cref="RunStatus.Failed"/> (resume and materialisation depend on it) but still delivers
/// its best draft; the outcome tells those cases apart. Stored as an integer.
/// </summary>
public enum RunOutcome
{
    /// <summary>All reviewers approved; the converged text was delivered.</summary>
    Converged = 0,

    /// <summary>The run stopped without full convergence and delivered its best available draft.</summary>
    DeliveredWithOpenPoints = 1,

    /// <summary>The run ended without a usable text.</summary>
    Failed = 2,

    /// <summary>The run was cancelled by the user or stopped by the service.</summary>
    Aborted = 3,
}
