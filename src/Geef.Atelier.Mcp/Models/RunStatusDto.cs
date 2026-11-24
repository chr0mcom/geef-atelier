namespace Geef.Atelier.Mcp.Models;

/// <summary>Status of a run. <c>Warnings</c> and <c>Outcome</c> are additive (Step47); older clients ignore them.</summary>
/// <param name="RunId">Run id.</param>
/// <param name="Status">Lifecycle status.</param>
/// <param name="ErrorMessage">Failure reason, if any.</param>
/// <param name="Warnings">Problems the run worked around; empty when none.</param>
/// <param name="Outcome">
/// What the run delivered once it ended (<c>Converged</c>, <c>DeliveredWithOpenPoints</c>, <c>Failed</c>,
/// <c>Aborted</c>), set only after the text and the finishing steps are saved; <c>null</c> while the run is still
/// working (also when its status already looks terminal) or for runs that ended before outcomes were recorded.
/// </param>
public sealed record RunStatusDto(
    string RunId,
    string Status,
    string? ErrorMessage,
    IReadOnlyList<RunWarningDto>? Warnings = null,
    string? Outcome = null);
