using System.Text.RegularExpressions;

namespace Geef.Atelier.Application.Orders;

/// <summary>The briefing split back into what the composer put in; lines keep their order, the answer marks are removed.</summary>
public sealed record BriefParts(string Request, IReadOnlyList<string> PersonAnswers, IReadOnlyList<string> AssistantAnswers,
    IReadOnlyList<string> FormAssumptions, IReadOnlyList<string> OpenFacts, string? Profile);

/// <summary>
/// Reads the parts of a briefing built by <see cref="IOrderBriefComposer"/>: the request comes first and unchanged, the blocks
/// of the intake (answers, corrections, assumptions) and the profile follow after <see cref="Separator"/>. Briefings that were
/// not built this way come back unchanged.
/// </summary>
public static partial class BriefingSections
{
    /// <summary>Separator between the request and the blocks the composer adds.</summary>
    public const string Separator = "\n\n---\n";

    /// <summary>Heading that starts the profile block.</summary>
    public const string ProfileHeading = IntakeBriefComposer.ProfileHeading;

    /// <summary>Line shown instead of the profile block to someone who is not the owner of the order, and after the account of the owner is gone.</summary>
    public const string ProfileHiddenNote = "(The writer also got the About me details of the person who ordered this text. They are hidden here.)";

    private static readonly string[] BlockStarts =
    [
        IntakeBriefComposer.AnswersHeading,
        IntakeBriefComposer.CorrectionsHeading,
        IntakeBriefComposer.AssumptionsHeadingStart,
        IntakeBriefComposer.AssumptionsNotShownHeadingStart,
        IntakeBriefComposer.OpenFactsHeadingStart,
        IntakeBriefComposer.ProfileHeading,
        IntakeBriefComposer.UnderstandingLead,
        RevisionBrief.Heading,
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

    /// <summary>
    /// Splits <paramref name="briefing"/> into the request and the blocks of the composer; a briefing not built by the composer is all
    /// request. Answers keep their question (<c>question → answer</c>); of a correction only the person's words remain, because the
    /// corrected assumption is not something the person said. The lines of the block with the changes the person asked for
    /// (<see cref="RevisionBrief.Heading"/>) are words of the person too, without their version mark. The summary of a prepared brief
    /// belongs to no part.
    /// </summary>
    public static BriefParts Parse(string briefing)
    {
        briefing ??= string.Empty;
        var request = RequestPart(briefing);
        if (request.Length == briefing.Length)
            return new BriefParts(briefing, [], [], [], [], null);

        var person = new List<string>();
        var assistant = new List<string>();
        var form = new List<string>();
        var open = new List<string>();
        string? profile = null;
        var block = BriefBlock.None;
        var lines = briefing[(request.Length + Separator.Length)..].Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line == IntakeBriefComposer.ProfileHeading)
            {
                profile = string.Join('\n', lines.Skip(i + 1)).Trim();
                break;
            }
            if (BlockOf(line) is { } next)
            {
                block = next;
                continue;
            }
            if (!line.StartsWith(ItemStart, StringComparison.Ordinal))
                continue;

            var item = line[ItemStart.Length..];
            switch (block)
            {
                case BriefBlock.Answers or BriefBlock.Corrections:
                    var byAssistant = item.EndsWith(IntakeBriefComposer.ByAssistant, StringComparison.Ordinal);
                    if (byAssistant)
                        item = item[..^IntakeBriefComposer.ByAssistant.Length];
                    if (block == BriefBlock.Corrections && item.IndexOf(AnswerArrow, StringComparison.Ordinal) is var arrow and >= 0)
                        item = item[(arrow + AnswerArrow.Length)..];
                    if (!string.IsNullOrWhiteSpace(item))
                        (byAssistant ? assistant : person).Add(item.Trim());
                    break;
                case BriefBlock.FormAssumptions:
                    form.Add(item.Trim());
                    break;
                case BriefBlock.OpenFacts:
                    open.Add(item.Trim());
                    break;
                case BriefBlock.Changes:
                    var change = VersionMark().Replace(item, string.Empty).Trim();
                    if (change.Length > 0)
                        person.Add(change);
                    break;
            }
        }

        return new BriefParts(request, person, assistant, form, open, string.IsNullOrEmpty(profile) ? null : profile);
    }

    private const string ItemStart = "- ";
    private const string AnswerArrow = " → ";

    private enum BriefBlock { None, Understanding, Answers, Corrections, FormAssumptions, OpenFacts, Changes }

    private static BriefBlock? BlockOf(string line) => line switch
    {
        _ when line == IntakeBriefComposer.AnswersHeading => BriefBlock.Answers,
        _ when line == IntakeBriefComposer.CorrectionsHeading => BriefBlock.Corrections,
        _ when line.StartsWith(IntakeBriefComposer.AssumptionsHeadingStart, StringComparison.Ordinal)
               || line.StartsWith(IntakeBriefComposer.AssumptionsNotShownHeadingStart, StringComparison.Ordinal) => BriefBlock.FormAssumptions,
        _ when line.StartsWith(IntakeBriefComposer.OpenFactsHeadingStart, StringComparison.Ordinal) => BriefBlock.OpenFacts,
        _ when line.StartsWith(IntakeBriefComposer.UnderstandingLead, StringComparison.Ordinal) => BriefBlock.Understanding,
        _ when line == RevisionBrief.Heading => BriefBlock.Changes,
        _ => null,
    };

    [GeneratedRegex(@"^\(version \d+\) ", RegexOptions.CultureInvariant)]
    private static partial Regex VersionMark();

    /// <summary>The briefing as <paramref name="viewer"/> may see it: complete for the owner, <see cref="WithoutProfile"/> for anyone else (also administrators).</summary>
    public static string ForViewer(string briefing, string? owner, string? viewer) =>
        owner is not null && string.Equals(owner, viewer, StringComparison.Ordinal) ? briefing : WithoutProfile(briefing);
}
