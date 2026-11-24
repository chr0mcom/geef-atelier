namespace Geef.Atelier.Mcp.Models;

/// <summary>How an attached file went into the text.</summary>
/// <param name="FileName">File name.</param>
/// <param name="Mode"><c>full_text</c>, <c>full_text_with_map</c> (very long files, read with a map) or <c>excluded</c>.</param>
/// <param name="Characters">Characters of the file that were passed on.</param>
public sealed record OrderSourceUsageDto(string FileName, string Mode, int Characters);
