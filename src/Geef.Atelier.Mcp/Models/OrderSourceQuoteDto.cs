namespace Geef.Atelier.Mcp.Models;

/// <summary>A passage the writer quoted from a file, verified against that file. Shows the file was read and quoted, not that every statement is backed.</summary>
/// <param name="File">Name of the file.</param>
/// <param name="Quote">The passage, word for word.</param>
public sealed record OrderSourceQuoteDto(string File, string Quote);
