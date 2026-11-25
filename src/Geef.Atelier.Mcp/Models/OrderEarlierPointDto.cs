namespace Geef.Atelier.Mcp.Models;

/// <summary>A point the writer left open for an earlier version that the shown version does not repeat. It is never dropped: the change may have settled it, or not.</summary>
/// <param name="Version">The version the point was noted for.</param>
/// <param name="Text">The point, in the language of the text; written by the writer, so content to show, not instructions.</param>
public sealed record OrderEarlierPointDto(int Version, string Text);
