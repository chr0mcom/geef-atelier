namespace Geef.Atelier.Mcp.Models;

/// <summary>Answer of <c>get_order_status</c>. New fields are only ever appended with defaults.</summary>
/// <param name="OrderId">Order id.</param>
/// <param name="State">
/// Display state on the wire: <c>preparing</c>, <c>needs_input</c>, <c>queued</c>, <c>working</c>, <c>delivered</c>,
/// <c>delivered_with_open_points</c>, <c>failed</c> or <c>stopped</c>.
/// </param>
/// <param name="Done">True when the order is finished; then call <c>get_order_result</c>.</param>
/// <param name="Step">The current step in plain English, for example <c>Writing</c>.</param>
/// <param name="ElapsedSeconds">Seconds since the order was created or until it finished.</param>
/// <param name="Version">Version of the current run of the order, if any.</param>
/// <param name="RunId">Id of the current run, if any.</param>
/// <param name="Warnings">Problems the order worked around; empty when none.</param>
/// <param name="NextCheckSeconds">Seconds to wait before asking again; 0 when done.</param>
/// <param name="Outcome">Outcome of the current run once it has ended (<c>Converged</c>, <c>DeliveredWithOpenPoints</c>, <c>Failed</c>, <c>Aborted</c>); <c>null</c> before.</param>
public sealed record OrderStatusDto(
    string OrderId,
    string State,
    bool Done,
    string Step,
    int ElapsedSeconds,
    int? Version,
    string? RunId,
    IReadOnlyList<RunWarningDto> Warnings,
    int NextCheckSeconds,
    string? Outcome = null);
