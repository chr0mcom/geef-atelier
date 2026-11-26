using System.Globalization;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>
/// The block of the briefing that carries the changes the person asked for: placed before the block with the About me details, one line
/// per version, newest last. Only the newest line is the change to make now (<see cref="NowLabelFormat"/>); every older line is marked as
/// already made in the text (<see cref="EarlierLabelFormat"/>), so that it is kept and not made again. The heading says that the words of the
/// person here take precedence over the blocks with the assumptions and the facts not given by the person.
/// </summary>
public static partial class RevisionBrief
{
    /// <summary>Heading of the block, word for word.</summary>
    public const string Heading = "Change requested by the person (newest last; where changes differ, the newest wins; what the person says here takes precedence over the blocks "
        + "\"Assumptions …\" and \"Not given by the person\"):";

    /// <summary>Label of the one line that is the change to make now: <c>{0}</c> is its version.</summary>
    public const string NowLabelFormat = "version {0}, the change to make now";

    /// <summary>Label of an older line: <c>{0}</c> is its version.</summary>
    public const string EarlierLabelFormat = "version {0}, earlier change: already made in the text – do not make it again, keep it";

    /// <summary>One line of the block: <c>{0}</c> is the label, <c>{1}</c> the change.</summary>
    public const string LineFormat = "- ({0}) {1}";

    /// <summary>Between the names of several quick buttons in <see cref="Requested"/>.</summary>
    public const string QuickSeparator = ", ";

    private const string BlockSeparator = "\n\n";
    private const string ProfileStart = IntakeBriefComposer.ProfileHeading + "\n";

    /// <summary>
    /// The briefing of version <paramref name="version"/>: <paramref name="baseBriefing"/> (the briefing of the shown version) with the
    /// line for <paramref name="change"/> added to the block as the change to make now, which is created before the block with the About
    /// me details when missing. The lines already in the block keep their version and words and are marked as earlier changes. The
    /// request and every other block stay as they are; the change is written on one line.
    /// </summary>
    public static string Compose(string baseBriefing, string change, int version)
    {
        ArgumentNullException.ThrowIfNull(baseBriefing);
        var oneLine = OneLine(change);
        if (oneLine.Length == 0)
            throw new ArgumentException("The change must not be empty.", nameof(change));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        var request = BriefingSections.RequestPart(baseBriefing);
        if (request.Length == baseBriefing.Length)
            return baseBriefing + BriefingSections.Separator + Block([], new RevisionChange(version, oneLine));

        var rest = baseBriefing[(request.Length + BriefingSections.Separator.Length)..];
        var profileAt = rest.StartsWith(ProfileStart, StringComparison.Ordinal)
            ? 0
            : rest.IndexOf(BlockSeparator + ProfileStart, StringComparison.Ordinal) is var found and >= 0 ? found + BlockSeparator.Length : -1;
        var before = profileAt < 0 ? rest : rest[..profileAt];
        var profile = profileAt < 0 ? null : rest[profileAt..];

        var trimmed = before.EndsWith(BlockSeparator, StringComparison.Ordinal) ? before[..^BlockSeparator.Length] : before;
        List<string> blocks = trimmed.Length == 0 ? [] : [.. trimmed.Split(BlockSeparator)];
        var existing = blocks.FindLastIndex(b => b.StartsWith(Heading + "\n", StringComparison.Ordinal));
        var next = new RevisionChange(version, oneLine);
        if (existing >= 0)
            blocks[existing] = Block(Lines(blocks[existing]).Select(c => c with { Words = WithoutRetired(c.Words) }).Where(c => c.Words.Length > 0).ToList(), next);
        else
            blocks.Add(Block([], next));
        if (profile is not null)
            blocks.Add(profile);

        return request + BriefingSections.Separator + string.Join(BlockSeparator, blocks);
    }

    /// <summary>
    /// The changes of a line of the block, without its mark: the version (<c>null</c> when the line carries none) and the words of the
    /// person; <c>null</c> for a line that is no change.
    /// </summary>
    public static RevisionChange? ReadLine(string line)
    {
        if (line is null || !line.StartsWith(ItemStart, StringComparison.Ordinal))
            return null;
        var item = line[ItemStart.Length..];
        var mark = Mark().Match(item);
        var words = (mark.Success ? item[mark.Length..] : item).Trim();
        if (words.Length == 0)
            return null;
        return new RevisionChange(mark.Success && int.TryParse(mark.Groups["version"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null, words);
    }

    /// <summary>
    /// The change without the fixed sentences of quick buttons that are no longer offered (<see cref="RevisionQuickText.Offered"/>), on one
    /// line; empty when nothing else was asked. Stored changes keep their words; only what goes to the writer again is cleaned, so a retired
    /// sentence never reaches the writer as an earlier change or through resuming.
    /// </summary>
    public static string WithoutRetired(string? change)
    {
        var words = OneLine(change);
        foreach (var quick in Enum.GetValues<RevisionQuick>().Where(q => !q.IsOffered()))
            words = OneLine(words.Replace(quick.Instruction(), " ", StringComparison.Ordinal));
        return words;
    }

    private static IReadOnlyList<RevisionChange> Lines(string block) =>
        block.Split('\n').Skip(1).Select(ReadLine).OfType<RevisionChange>().ToList();

    private static string Block(IReadOnlyList<RevisionChange> earlier, RevisionChange now)
    {
        var lines = earlier.Select(c => Line(EarlierLabelFormat, c)).Append(Line(NowLabelFormat, now));
        return Heading + "\n" + string.Join('\n', lines);
    }

    private static string Line(string labelFormat, RevisionChange change) =>
        string.Format(CultureInfo.InvariantCulture, LineFormat,
            string.Format(CultureInfo.InvariantCulture, labelFormat, change.Version?.ToString(CultureInfo.InvariantCulture) ?? "?"), change.Words);

    /// <summary>
    /// The change as sent to the writer: the trimmed words of the person (on one line, every run of white space as one space) and then
    /// the sentences of the quick buttons, each once; empty when there is neither.
    /// </summary>
    public static string Change(string? text, IReadOnlyList<RevisionQuick> quick)
    {
        var parts = new List<string>();
        var words = OneLine(text);
        if (words.Length > 0)
            parts.Add(words);
        parts.AddRange((quick ?? []).Distinct().Select(q => q.Instruction()));
        return string.Join(' ', parts);
    }

    /// <summary>
    /// What the person asked for, as shown under the versions: their words, followed by the names of the quick buttons in brackets
    /// (only the names when there are no words). Read back from a change built by <see cref="Change"/>; <c>null</c> when there is none.
    /// </summary>
    public static string? Requested(string? change)
    {
        if (string.IsNullOrWhiteSpace(change))
            return null;

        var rest = change.Trim();
        var labels = new List<string>();
        var found = true;
        while (found && rest.Length > 0)
        {
            found = false;
            foreach (var quick in Enum.GetValues<RevisionQuick>())
            {
                var sentence = quick.Instruction();
                if (!rest.EndsWith(sentence, StringComparison.Ordinal))
                    continue;
                var start = rest.Length - sentence.Length;
                if (start > 0 && rest[start - 1] != ' ')
                    continue;
                labels.Insert(0, quick.Label());
                rest = rest[..start].TrimEnd();
                found = true;
                break;
            }
        }

        var names = string.Join(QuickSeparator, labels);
        return (rest.Length, labels.Count) switch
        {
            (0, _) => names,
            (_, 0) => rest,
            _ => $"{rest} ({names})",
        };
    }

    private const string ItemStart = "- ";

    private static string OneLine(string? text) => text is null ? string.Empty : Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^\(version (?<version>\d+)[^)]*\) ", RegexOptions.CultureInvariant)]
    private static partial Regex Mark();
}

/// <summary>One change of the person as it stands in the block of <see cref="RevisionBrief"/>: its <see cref="Version"/> and the <see cref="Words"/>.</summary>
public sealed record RevisionChange(int? Version, string Words);
