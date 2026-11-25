using System.Globalization;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>
/// The block of the briefing that carries the changes the person asked for: placed before the block with the About me details, one line
/// per version, older changes kept, newest last.
/// </summary>
public static partial class RevisionBrief
{
    /// <summary>Heading of the block, word for word.</summary>
    public const string Heading = "Change requested by the person (newest last; where changes differ, the newest wins):";

    /// <summary>One line of the block: <c>{0}</c> is the version, <c>{1}</c> the change.</summary>
    public const string LineFormat = "- (version {0}) {1}";

    /// <summary>Between the names of several quick buttons in <see cref="Requested"/>.</summary>
    public const string QuickSeparator = ", ";

    private const string BlockSeparator = "\n\n";
    private const string ProfileStart = IntakeBriefComposer.ProfileHeading + "\n";

    /// <summary>
    /// The briefing of version <paramref name="version"/>: <paramref name="baseBriefing"/> (the briefing of the shown version) with the
    /// line for <paramref name="change"/> added to the block, which is created before the block with the About me details when missing.
    /// The request and every other block stay as they are; the change is written on one line.
    /// </summary>
    public static string Compose(string baseBriefing, string change, int version)
    {
        ArgumentNullException.ThrowIfNull(baseBriefing);
        var oneLine = OneLine(change);
        if (oneLine.Length == 0)
            throw new ArgumentException("The change must not be empty.", nameof(change));
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        var line = string.Format(CultureInfo.InvariantCulture, LineFormat, version, oneLine);
        var request = BriefingSections.RequestPart(baseBriefing);
        if (request.Length == baseBriefing.Length)
            return baseBriefing + BriefingSections.Separator + Heading + "\n" + line;

        var rest = baseBriefing[(request.Length + BriefingSections.Separator.Length)..];
        var profileAt = rest.StartsWith(ProfileStart, StringComparison.Ordinal)
            ? 0
            : rest.IndexOf(BlockSeparator + ProfileStart, StringComparison.Ordinal) is var found and >= 0 ? found + BlockSeparator.Length : -1;
        var before = profileAt < 0 ? rest : rest[..profileAt];
        var profile = profileAt < 0 ? null : rest[profileAt..];

        var trimmed = before.EndsWith(BlockSeparator, StringComparison.Ordinal) ? before[..^BlockSeparator.Length] : before;
        List<string> blocks = trimmed.Length == 0 ? [] : [.. trimmed.Split(BlockSeparator)];
        var existing = blocks.FindLastIndex(b => b.StartsWith(Heading + "\n", StringComparison.Ordinal) || b == Heading);
        if (existing >= 0)
            blocks[existing] = blocks[existing] + "\n" + line;
        else
            blocks.Add(Heading + "\n" + line);
        if (profile is not null)
            blocks.Add(profile);

        return request + BriefingSections.Separator + string.Join(BlockSeparator, blocks);
    }

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

    private static string OneLine(string? text) => text is null ? string.Empty : Whitespace().Replace(text, " ").Trim();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();
}
