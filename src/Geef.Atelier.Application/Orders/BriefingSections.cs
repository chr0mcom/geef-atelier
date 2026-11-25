namespace Geef.Atelier.Application.Orders;

/// <summary>
/// Reads the parts of a briefing built by <see cref="IOrderBriefComposer"/>: the request comes first and unchanged, the blocks
/// of the intake (answers, corrections, assumptions) and the profile follow after <see cref="Separator"/>. Briefings that were
/// not built this way come back unchanged.
/// </summary>
public static class BriefingSections
{
    /// <summary>Separator between the request and the blocks the composer adds.</summary>
    public const string Separator = "\n\n---\n";

    /// <summary>Line shown instead of the profile block to someone who is not the owner of the order.</summary>
    public const string ProfileHiddenNote = "(The writer also got the About me details of the person who ordered this text. They are hidden here.)";

    private static readonly string[] BlockStarts =
    [
        IntakeBriefComposer.AnswersHeading,
        IntakeBriefComposer.CorrectionsHeading,
        IntakeBriefComposer.AssumptionsHeadingStart,
        IntakeBriefComposer.ProfileHeading,
        IntakeBriefComposer.UnderstandingLead,
    ];

    /// <summary>
    /// Only the request as the person wrote it: everything from the first separator that starts a composer block on is cut.
    /// External research, query shaping and refinement get only this text, never answers, assumptions or the profile.
    /// </summary>
    public static string RequestPart(string briefing)
    {
        if (string.IsNullOrEmpty(briefing))
            return briefing;
        var from = 0;
        while (true)
        {
            var index = briefing.IndexOf(Separator, from, StringComparison.Ordinal);
            if (index < 0)
                return briefing;
            var rest = briefing.AsSpan(index + Separator.Length);
            foreach (var start in BlockStarts)
            {
                if (rest.StartsWith(start, StringComparison.Ordinal))
                    return briefing[..index];
            }
            from = index + 1;
        }
    }

    /// <summary>The briefing without the profile block (the last block), with <see cref="ProfileHiddenNote"/> in its place.</summary>
    public static string WithoutProfile(string briefing)
    {
        if (string.IsNullOrEmpty(briefing))
            return briefing;
        var index = briefing.LastIndexOf("\n" + IntakeBriefComposer.ProfileHeading + "\n", StringComparison.Ordinal);
        if (index < 0)
            return briefing;
        var before = briefing[..index].TrimEnd('\n');
        if (before.EndsWith("\n---", StringComparison.Ordinal))
            before = before[..^4].TrimEnd('\n');
        return before + "\n\n" + ProfileHiddenNote;
    }

    /// <summary>The briefing as <paramref name="viewer"/> may see it: complete for the owner, <see cref="WithoutProfile"/> for anyone else (also administrators).</summary>
    public static string ForViewer(string briefing, string? owner, string? viewer) =>
        owner is not null && string.Equals(owner, viewer, StringComparison.Ordinal) ? briefing : WithoutProfile(briefing);
}
