namespace Geef.Atelier.Mcp.Models;

/// <summary>Receipt for one attached file as it was read.</summary>
/// <param name="FileName">Cleaned file name.</param>
/// <param name="Pages">Number of pages (PDF only).</param>
/// <param name="Characters">Characters of readable text.</param>
/// <param name="Quality">How well the text could be read: <c>good</c>, <c>partial</c> or <c>poor</c>.</param>
/// <param name="Warnings">Plain-English problems found while reading the file; empty when none.</param>
public sealed record OrderSourceDto(string FileName, int? Pages, int Characters, string Quality, IReadOnlyList<string> Warnings);
