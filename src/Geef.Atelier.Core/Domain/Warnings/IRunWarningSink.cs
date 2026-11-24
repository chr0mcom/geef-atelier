namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>
/// Records warnings against a run. Every write uses its own short-lived database scope, so a failed write can
/// never poison the run's shared <c>DbContext</c>; write errors are logged and swallowed, a warning never stops
/// or fails a run.
/// </summary>
public interface IRunWarningSink
{
    /// <summary>Stores one warning. Never throws, except for cancellation of <paramref name="ct"/>.</summary>
    /// <param name="runId">Run the warning belongs to.</param>
    /// <param name="source">Reporting part of the system, see <see cref="RunWarningSources"/>.</param>
    /// <param name="code">Stable code, see <see cref="RunWarningCodes"/>.</param>
    /// <param name="message">Plain-English sentence for the user; truncated to a safe length.</param>
    /// <param name="iteration">Iteration number when the warning belongs to one round, otherwise <c>null</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task ReportAsync(Guid runId, string source, string code, string message, int? iteration = null, CancellationToken ct = default);
}
