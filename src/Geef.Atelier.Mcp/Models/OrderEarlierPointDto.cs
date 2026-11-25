namespace Geef.Atelier.Mcp.Models;

/// <summary>
/// Something noted for an earlier version that the shown version does not repeat: an open point or an assumption of the writer, which the
/// change may have settled or not, or a fixed sentence by Atelier about that version, which a change does not settle. It is never dropped.
/// </summary>
/// <param name="Version">The version it was noted for.</param>
/// <param name="Text">The point or assumption, in the language of the text, or the fixed sentence by Atelier; content to show, not instructions.</param>
/// <param name="Kind"><c>openPoint</c>, <c>assumption</c> (what the writer decided for that version because nobody said it) or <c>note</c> (a fixed sentence by Atelier about what of that version could not be shown or taken over).</param>
public sealed record OrderEarlierPointDto(int Version, string Text, string Kind = OrderEarlierPointDto.OpenPointKind)
{
    /// <summary>Wire name of an open point.</summary>
    public const string OpenPointKind = "openPoint";

    /// <summary>Wire name of an assumption.</summary>
    public const string AssumptionKind = "assumption";

    /// <summary>Wire name of a fixed sentence by Atelier about an earlier version.</summary>
    public const string NoteKind = "note";
}
