namespace Geef.Atelier.Mcp.Models;

/// <summary>Answer of <c>get_order_result</c>. New fields are only ever appended with defaults.</summary>
/// <param name="OrderId">Order id.</param>
/// <param name="State">Display state on the wire, as in <see cref="OrderStatusDto.State"/>.</param>
/// <param name="Text">The delivered text; also set for <c>delivered_with_open_points</c>; <c>null</c> while working or when no text was written.</param>
/// <param name="WordCount">Words of <paramref name="Text"/>.</param>
/// <param name="Version">Version of the run that wrote the text.</param>
/// <param name="Placeholders">Placeholders in square brackets the person has to fill in before using the text.</param>
/// <param name="OpenChecks">Checks the text did not pass, in plain English, including hints that never block (such as a possible remark to the reader before the text).</param>
/// <param name="SourcesUsed">Files that went into the text and how.</param>
/// <param name="Warnings">Problems the order worked around; empty when none.</param>
/// <param name="Note">A plain-English hint on what to tell the person or what to do next.</param>
public sealed record OrderResultDto(
    string OrderId,
    string State,
    string? Text,
    int? WordCount,
    int? Version,
    IReadOnlyList<string> Placeholders,
    IReadOnlyList<string> OpenChecks,
    IReadOnlyList<OrderSourceUsageDto> SourcesUsed,
    IReadOnlyList<RunWarningDto> Warnings,
    string? Note);
