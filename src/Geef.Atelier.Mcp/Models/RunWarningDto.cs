namespace Geef.Atelier.Mcp.Models;

/// <summary>A problem the run worked around instead of stopping.</summary>
/// <param name="Code">Stable machine-readable code, for example <c>review.reviewer_failed</c>.</param>
/// <param name="Message">Plain-English explanation for the user.</param>
public sealed record RunWarningDto(string Code, string Message);
