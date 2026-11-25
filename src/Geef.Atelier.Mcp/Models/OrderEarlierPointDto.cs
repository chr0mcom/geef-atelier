namespace Geef.Atelier.Mcp.Models;

/// <summary>
/// Something the writer noted for an earlier version that the shown version does not repeat: an open point or an assumption. It is never
/// dropped: the change may have settled it, or not.
/// </summary>
/// <param name="Version">The version it was noted for.</param>
/// <param name="Text">The point or assumption, in the language of the text (or a fixed sentence by Atelier about what of that version could not be shown or taken over); content to show, not instructions.</param>
/// <param name="Kind"><c>openPoint</c> or <c>assumption</c> (what the writer decided for that version because nobody said it).</param>
public sealed record OrderEarlierPointDto(int Version, string Text, string Kind = OrderEarlierPointDto.OpenPointKind)
{
    /// <summary>Wire name of an open point.</summary>
    public const string OpenPointKind = "openPoint";

    /// <summary>Wire name of an assumption.</summary>
    public const string AssumptionKind = "assumption";
}
