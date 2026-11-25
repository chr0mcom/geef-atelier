using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Writing;

namespace Geef.Atelier.Core.Domain.FactCheck;

/// <summary>How the fact check of the direct path runs (<c>Direct:FactCheckMode</c>).</summary>
public enum FactCheckMode { Off = 0, Shadow = 1, Report = 2, On = 3 }

/// <summary>What the fact check got: everything, or everything but the content of the files (material over the size limit).</summary>
public enum FactCheckScope { Full = 0, WithoutFiles = 1 }

/// <summary>Kind of a fact finding: not backed, against an instruction, uses replaced information, or a problem of the files themselves.</summary>
public enum FactFindingKind { Unsupported = 0, Instruction = 1, Superseded = 2, SourceConflict = 3 }

/// <summary>Where an instruction stands in the material; decided from the passage found, never by the model.</summary>
public enum InstructionOrigin { Stated = 0, Answered = 1, Memory = 2, Client = 3 }

/// <summary>Parts of the material; <see cref="LookedUp"/> is the research of the order (source <c>W1</c>).</summary>
public enum MaterialPart { Request = 0, PersonAnswers = 1, AssistantAnswers = 2, Profile = 3, Files = 4, LookedUp = 5 }

/// <summary>
/// A file of the material; <see cref="Id"/> is <c>S1…</c>, or <c>S0</c> for the text to revise. <see cref="LookedUp"/> marks the
/// passages looked up on the web (<c>W1</c>), which are not a file of the person.
/// </summary>
public sealed record FactSource(string Id, string FileName, string Text, bool LookedUp = false);

/// <summary>
/// Everything the text may rely on. Assumptions and the writer's notes are deliberately not part of it. <see cref="Changes"/> are the changes
/// the person asked for when the run changes an earlier version (in their order, the newest last; empty for the first run of an order): words
/// of the person, kept apart from <see cref="PersonAnswers"/> so that the mark that says which change is the newest (<see cref="FactChange.Marked"/>)
/// is only ever added where the material is written out for the check, never to what a quote is looked up in.
/// </summary>
public sealed record FactMaterial(string Request, IReadOnlyList<string> PersonAnswers, IReadOnlyList<string> AssistantAnswers, string? Profile,
    IReadOnlyList<FactSource> Files, FactCheckScope Scope, string? FileMap, IReadOnlyList<FactChange>? Changes = null)
{
    /// <summary>Characters of all parts together as the check reads them, the files and the marks of the changes included.</summary>
    public int TotalChars { get; } = Request.Length
        + PersonAnswers.Sum(a => a.Length)
        + (Changes ?? []).Sum(c => c.Marked.Length)
        + AssistantAnswers.Sum(a => a.Length)
        + (Profile?.Length ?? 0)
        + Files.Sum(f => f.Text.Length)
        + (FileMap?.Length ?? 0);

    /// <summary>True when the run changes an earlier version: the person asked for at least one change.</summary>
    public bool IsRevision => Changes is { Count: > 0 };
}

/// <summary>
/// A change the person asked for: its <see cref="Version"/> (<c>null</c> when the briefing names none), the <see cref="Words"/> of the person
/// and whether it is the <see cref="Newest"/>, the change made now. The mark before the words (<see cref="NewestMarkFormat"/>,
/// <see cref="EarlierMarkFormat"/>) is Atelier's, never a word of the person: it is written out only for the check (<see cref="Marked"/>)
/// and taken out of every quote of the check (<see cref="WithoutMarks"/>).
/// </summary>
public sealed partial record FactChange(int? Version, string Words, bool Newest)
{
    /// <summary>Mark before the newest change: <c>{0}</c> is its version.</summary>
    public const string NewestMarkFormat = "(newest change, version {0}) ";

    /// <summary>Mark before an earlier change: <c>{0}</c> is its version.</summary>
    public const string EarlierMarkFormat = "(earlier change, version {0}; a newer change wins where they differ) ";

    /// <summary>The mark of this change, with its trailing space.</summary>
    public string Mark => string.Format(CultureInfo.InvariantCulture, Newest ? NewestMarkFormat : EarlierMarkFormat,
        Version?.ToString(CultureInfo.InvariantCulture) ?? "?");

    /// <summary>The line as the check reads it under the answers of the person: <see cref="Mark"/> and then the words.</summary>
    public string Marked => Mark + Words;

    /// <summary>
    /// <paramref name="quote"/> without any mark of a change: a whole mark anywhere, and the end of a mark the quote starts with (for example
    /// <c>version 2) …</c>), are taken out; <c>null</c> stays <c>null</c>. A quote that was only a mark comes back blank.
    /// </summary>
    public static string? WithoutMarks(string? quote)
    {
        if (string.IsNullOrEmpty(quote))
            return quote;
        var rest = WholeMark().Replace(quote, " ");
        rest = MarkEnd().Replace(rest, string.Empty);
        return rest == quote ? quote : string.Join(' ', rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    [GeneratedRegex(@"\(\s*(?:newest|earlier)\s+change\s*,\s*version\s+(?:\d+|\?)\s*(?:;\s*a\s+newer\s+change\s+wins\s+where\s+they\s+differ\s*)?\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex WholeMark();

    [GeneratedRegex(@"^[^()]{0,80}?(?:\bversion\s+(?:\d+|\?)|\bwhere\s+they\s+differ)\s*\)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkEnd();
}

/// <summary>A finding as the model proposed it, before the code checked it.</summary>
public sealed record ProposedFactFinding(FactFindingKind Kind, string? TextQuote, string WhatIsWrong, string? InstructionQuote, string? MaterialQuote, string? MaterialQuote2);

/// <summary>
/// What the filter decided about a proposed finding; the first matching rule decides. <see cref="DroppedUnsafe"/> is no longer
/// given: a finding with an unsafe reason is kept with <see cref="FactFindingFilter.UnsafeReason"/> as its reason.
/// </summary>
public enum FactFilterDecision
{
    Kept = 0, DroppedTooShort = 1, DroppedQuoteNotInText = 2, DroppedPlaceholder = 3, DroppedCoveredVerbatim = 4,
    DroppedInstructionNotFound = 5, DroppedInstructionFromFile = 6, DroppedUnsafe = 7, DroppedDuplicate = 8, DroppedOverLimit = 9,
    DroppedMaterialQuoteNotFound = 10, DroppedFileClaimWithoutFiles = 11,
}

/// <summary>
/// A finding after the code checked it. Ids are <c>f1…</c>, given by the filter. <see cref="TextQuote"/> is null only for a source
/// conflict the text does not use. <see cref="MaterialFrom"/> and <see cref="Material2From"/> say in which part of the material the
/// code found <see cref="MaterialQuote"/> and <see cref="MaterialQuote2"/>, <see cref="Material2File"/> the file of the second quote
/// (<c>null</c> in reports written before they were kept). <see cref="FromVersion"/> is the version that found a finding carried over from
/// the report of an earlier version (<c>null</c> for one of this version). <see cref="BeforeLastChange"/> marks a finding against an
/// instruction in a run that changes an earlier version whose instruction does not stand in the newest change (it stands in the request,
/// the answers of the person or the assistant, the About me details or an earlier change): the newest change may have replaced it, so it
/// never blocks; written only when true. No quote of a finding ever holds the mark of a change (<see cref="FactChange.WithoutMarks"/>).
/// </summary>
public sealed record FactFinding(string Id, FactFindingKind Kind, string? TextQuote, string WhatIsWrong, string? InstructionQuote,
    string? MaterialQuote, string? MaterialQuote2, string? MaterialFile, InstructionOrigin? Origin, bool Blocking, QuoteMatchKind Match,
    MaterialPart? MaterialFrom = null, MaterialPart? Material2From = null, string? Material2File = null, int? FromVersion = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool BeforeLastChange = false);

/// <summary>Decision of the filter for the proposed finding at <see cref="Index"/>.</summary>
public sealed record FactFilterTrace(int Index, FactFilterDecision Decision);

/// <summary>Kept findings and the decision for every proposed one.</summary>
public sealed record FactFilterResult(IReadOnlyList<FactFinding> Findings, IReadOnlyList<FactFilterTrace> Trace);

/// <summary>
/// Checks proposed findings against the text and the material without an AI model (contract stage 3, 4.3). Whether a
/// finding stays, where an instruction comes from and whether a finding blocks follow from where its quotes are found,
/// never from what the model says about them. A finding about replaced information whose replacing passage stands in the pages
/// looked up on the web (<see cref="MaterialPart.LookedUp"/>) never blocks and never takes the place of another finding at the same
/// passage of the text (D-074). In a run that changes an earlier version (<see cref="FactMaterial.IsRevision"/>) a finding against an
/// instruction blocks only when its instruction stands in the newest change (also when it stands elsewhere as well); any other is kept
/// as a hint with <see cref="FactFinding.BeforeLastChange"/>. Every quote is taken without the marks of the changes
/// (<see cref="FactChange.WithoutMarks"/>) before it is looked up or kept, so a mark never counts as a word of the person.
/// </summary>
public static class FactFindingFilter
{
    public const int MaxBlocking = 8;

    /// <summary>
    /// Reason kept instead of the model's <c>what_is_wrong</c> when that names a link, an address, or account or access details
    /// (<see cref="IntakeContentGuard.IsUnsafe"/>). The finding itself always stays: its quote comes from the text, and an invented
    /// e-mail address or IBAN is exactly what the person must see.
    /// </summary>
    public const string UnsafeReason = "The reason given by the check is not repeated here because it names a link, an address, or account or access details.";

    /// <summary>Shown instead of a material quote that holds a link, an address, or account or access details.</summary>
    public const string UnsafeQuote = "[a passage with a link, an address, or account or access details, not shown here]";

    /// <summary>Shortest instruction quote that is looked up in the material; instructions such as "ohne Emojis" are short.</summary>
    public const int MinInstructionChars = 4;

    /// <summary>Shortest material quote of a source conflict or a replacing passage; a stated total such as "Summe 62 €" is short.</summary>
    public const int MinMaterialChars = 5;

    private const int MaxPlaceholderChars = 200;

    /// <summary>Applies the rules in their fixed order; every decision lands in the trace.</summary>
    public static FactFilterResult Apply(IReadOnlyList<ProposedFactFinding> proposed, string draft, FactMaterial material)
    {
        draft ??= string.Empty;
        proposed ??= [];
        var parts = material.Parts().ToList();
        var normalizedDraft = QuoteVerifier.Normalize(draft);
        var kept = new List<FactFinding>();
        var trace = new List<FactFilterTrace>();
        var ranges = new List<(int Start, int End)>();
        var hintRanges = new List<(int Start, int End)>();
        var conflicts = new List<(string A, string B)>();
        var blocking = 0;

        for (var index = 0; index < proposed.Count; index++)
        {
            var finding = proposed[index] is { } raw
                ? raw with
                {
                    InstructionQuote = FactChange.WithoutMarks(raw.InstructionQuote),
                    MaterialQuote = FactChange.WithoutMarks(raw.MaterialQuote),
                    MaterialQuote2 = FactChange.WithoutMarks(raw.MaterialQuote2),
                }
                : null;
            var (decision, result) = finding is null
                ? (FactFilterDecision.DroppedTooShort, (FactFinding?)null)
                : finding.Kind == FactFindingKind.SourceConflict
                    ? DecideConflict(finding, draft, material, parts, conflicts)
                    : DecideTextFinding(finding, draft, normalizedDraft, material, parts, ranges, hintRanges, blocking);

            if (decision == FactFilterDecision.Kept && result is not null)
            {
                kept.Add(result with { Id = $"f{kept.Count + 1}" });
                if (result.Blocking)
                    blocking++;
            }
            trace.Add(new FactFilterTrace(index, decision));
        }

        return new FactFilterResult(kept, trace);
    }

    private static (FactFilterDecision, FactFinding?) DecideTextFinding(ProposedFactFinding finding, string draft, string normalizedDraft,
        FactMaterial material, List<MaterialSlice> parts, List<(int Start, int End)> ranges, List<(int Start, int End)> hintRanges, int blocking)
    {
        var quote = finding.TextQuote?.Trim() ?? string.Empty;
        if (IsTooShort(quote))
            return (FactFilterDecision.DroppedTooShort, null);

        var match = QuoteVerifier.Find(draft, quote, allowApproximate: true);
        if (!match.IsFound)
            return (FactFilterDecision.DroppedQuoteNotInText, null);

        if (InPlaceholder(normalizedDraft, match.Start, match.Length))
            return (FactFilterDecision.DroppedPlaceholder, null);

        if (finding.Kind == FactFindingKind.Unsupported)
        {
            if (parts.Any(p => QuoteVerifier.ContainsWholeWords(p.Text, quote)))
                return (FactFilterDecision.DroppedCoveredVerbatim, null);
            if (material.Scope == FactCheckScope.WithoutFiles)
                return (FactFilterDecision.DroppedFileClaimWithoutFiles, null);
        }

        InstructionOrigin? origin = null;
        var beforeLastChange = false;
        if (finding.Kind == FactFindingKind.Instruction)
        {
            var (found, before, decision) = OriginOf(finding.InstructionQuote, parts, material.IsRevision);
            if (decision is not null)
                return (decision.Value, null);
            origin = found;
            beforeLastChange = before;
        }

        var reason = Reason(finding.WhatIsWrong, material);

        var slice = finding.Kind == FactFindingKind.Superseded ? FindMaterial(finding.MaterialQuote, parts) : null;
        var isHint = slice?.Part == MaterialPart.LookedUp;
        var range = (Start: match.Start, End: match.Start + Math.Max(1, match.Length));
        if (ranges.Any(r => r.Start < range.End && range.Start < r.End) || isHint && hintRanges.Any(r => r.Start < range.End && range.Start < r.End))
            return (FactFilterDecision.DroppedDuplicate, null);

        string? materialFile = null;
        MaterialPart? materialFrom = null;
        if (finding.Kind == FactFindingKind.Superseded)
        {
            if (slice is null)
                return (FactFilterDecision.DroppedMaterialQuoteNotFound, null);
            materialFile = slice.FileName;
            materialFrom = slice.Part;
        }

        var isBlocking = finding.Kind switch
        {
            FactFindingKind.Instruction => !beforeLastChange && origin is InstructionOrigin.Stated or InstructionOrigin.Answered or InstructionOrigin.Memory,
            FactFindingKind.Superseded => materialFrom != MaterialPart.LookedUp,
            _ => true,
        };
        if (isBlocking && blocking >= MaxBlocking)
            return (FactFilterDecision.DroppedOverLimit, null);

        (isHint ? hintRanges : ranges).Add(range);
        var excerpt = QuoteVerifier.ExcerptWholeWords(draft, match);
        return (FactFilterDecision.Kept, new FactFinding(string.Empty, finding.Kind, excerpt.Length > 0 ? excerpt : quote,
            reason, Trimmed(finding.InstructionQuote), SafeQuote(finding.MaterialQuote, material), SafeQuote(finding.MaterialQuote2, material),
            materialFile, origin, isBlocking, match.Kind, materialFrom, BeforeLastChange: beforeLastChange));
    }

    /// <summary>
    /// <paramref name="finding"/> as it counts in a run that changes an earlier version, whose material is <paramref name="material"/>: a
    /// finding against an instruction (also one carried over from an earlier version) blocks only when its instruction stands in the newest
    /// change of <paramref name="material"/>; any other, and every one when the material is unknown, is a hint with
    /// <see cref="FactFinding.BeforeLastChange"/>. Every other kind of finding stays as it is. Only for runs that change an earlier version.
    /// </summary>
    public static FactFinding ForRevision(FactFinding finding, FactMaterial? material)
    {
        if (finding is not { Kind: FactFindingKind.Instruction } || finding.BeforeLastChange)
            return finding;
        var quote = FactChange.WithoutMarks(finding.InstructionQuote)?.Trim() ?? string.Empty;
        var newest = quote.Length > 0 && material is not null && material.Parts().Any(p => p.Change is { Newest: true }
            && QuoteVerifier.FindVerbatim(p.Text, quote, MinInstructionChars).IsVerbatim);
        return newest ? finding : finding with { Blocking = false, BeforeLastChange = true };
    }

    private static (FactFilterDecision, FactFinding?) DecideConflict(ProposedFactFinding finding, string draft, FactMaterial material,
        List<MaterialSlice> parts, List<(string A, string B)> conflicts)
    {
        var a = Key(finding.MaterialQuote);
        var b = Key(finding.MaterialQuote2);
        if (a.Length > 0 && b.Length > 0 && conflicts.Any(c => Overlaps(c.A, a) && Overlaps(c.B, b) || Overlaps(c.A, b) && Overlaps(c.B, a)))
            return (FactFilterDecision.DroppedDuplicate, null);

        var first = FindMaterial(finding.MaterialQuote, parts);
        var second = FindMaterial(finding.MaterialQuote2, parts);
        if (first is null || second is null)
            return (FactFilterDecision.DroppedMaterialQuoteNotFound, null);

        conflicts.Add((a, b));
        string? textQuote = null;
        var kind = QuoteMatchKind.NotFound;
        var quote = finding.TextQuote?.Trim() ?? string.Empty;
        if (!IsTooShort(quote))
        {
            var match = QuoteVerifier.Find(draft, quote, allowApproximate: true);
            if (match.IsFound)
            {
                var excerpt = QuoteVerifier.ExcerptWholeWords(draft, match);
                textQuote = excerpt.Length > 0 ? excerpt : quote;
                kind = match.Kind;
            }
        }

        return (FactFilterDecision.Kept, new FactFinding(string.Empty, FactFindingKind.SourceConflict, textQuote, Reason(finding.WhatIsWrong, material),
            null, SafeQuote(finding.MaterialQuote, material), SafeQuote(finding.MaterialQuote2, material), first.FileName ?? second.FileName, null, false, kind,
            first.Part, second.Part, second.FileName));
    }

    /// <summary>
    /// Where the instruction quote stands, in the order request, answers of the person (the changes among them), About me details,
    /// answers of the assistant. In a run that changes an earlier version (<paramref name="revision"/>) the newest change is looked at
    /// first: a quote that stands there is an answer of the person from the newest change, wherever else it stands too; any other quote
    /// that is found comes from before the newest change (<c>BeforeLastChange</c>).
    /// </summary>
    private static (InstructionOrigin? Origin, bool BeforeLastChange, FactFilterDecision? Decision) OriginOf(string? instructionQuote,
        List<MaterialSlice> parts, bool revision)
    {
        var quote = instructionQuote?.Trim() ?? string.Empty;
        if (quote.Length == 0)
            return (null, false, FactFilterDecision.DroppedInstructionNotFound);

        bool Found(MaterialSlice slice) => QuoteVerifier.FindVerbatim(slice.Text, quote, MinInstructionChars).IsVerbatim;
        bool In(MaterialPart part) => parts.Any(p => p.Part == part && Found(p));

        if (revision && parts.Any(p => p.Change is { Newest: true } && Found(p)))
            return (InstructionOrigin.Answered, false, null);
        if (In(MaterialPart.Request))
            return (InstructionOrigin.Stated, revision, null);
        if (In(MaterialPart.PersonAnswers))
            return (InstructionOrigin.Answered, revision, null);
        if (In(MaterialPart.Profile))
            return (InstructionOrigin.Memory, revision, null);
        if (In(MaterialPart.AssistantAnswers))
            return (InstructionOrigin.Client, revision, null);
        if (In(MaterialPart.Files) || In(MaterialPart.LookedUp))
            return (null, false, FactFilterDecision.DroppedInstructionFromFile);
        return (null, false, FactFilterDecision.DroppedInstructionNotFound);
    }

    private static MaterialSlice? FindMaterial(string? quote, List<MaterialSlice> parts)
    {
        var trimmed = quote?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
            return null;
        return parts.FirstOrDefault(p => QuoteVerifier.FindVerbatim(p.Text, trimmed, MinMaterialChars).IsVerbatim);
    }

    private static bool IsTooShort(string quote)
    {
        var normalized = QuoteVerifier.Normalize(quote);
        return normalized.Length < QuoteVerifier.MinChars
            || normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetterOrDigit)) < QuoteVerifier.MinWords;
    }

    /// <summary>True when the passage at <paramref name="start"/> lies completely inside one placeholder in square brackets.</summary>
    internal static bool InPlaceholder(string normalizedDraft, int start, int length)
    {
        if (length <= 0 || start < 0 || start + length > normalizedDraft.Length)
            return false;
        var region = normalizedDraft.AsSpan(start, length).Trim();
        if (region.Length >= 2 && region[0] == '[' && region[^1] == ']' && region[1..^1].IndexOfAny('[', ']') < 0)
            return true;
        if (region.IndexOfAny('[', ']') >= 0)
            return false;

        var open = -1;
        for (var i = start - 1; i >= 0 && start - i <= MaxPlaceholderChars; i--)
        {
            var c = normalizedDraft[i];
            if (c == ']' || c == '\n')
                return false;
            if (c == '[')
            {
                open = i;
                break;
            }
        }
        if (open < 0)
            return false;
        for (var i = start + length; i < normalizedDraft.Length && i - open <= MaxPlaceholderChars; i++)
        {
            var c = normalizedDraft[i];
            if (c == '[' || c == '\n')
                return false;
            if (c == ']')
                return true;
        }
        return false;
    }

    private static string Key(string? quote) => QuoteVerifier.Normalize(quote ?? string.Empty).ToLowerInvariant();

    private static bool Overlaps(string left, string right) =>
        left.Length > 0 && right.Length > 0 && (left.Contains(right, StringComparison.Ordinal) || right.Contains(left, StringComparison.Ordinal));

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string Reason(string? whatIsWrong, FactMaterial material) =>
        IntakeContentGuard.IsUnsafe(whatIsWrong, material.Request) ? UnsafeReason : Shorten(whatIsWrong);

    private static string? SafeQuote(string? quote, FactMaterial material) =>
        Trimmed(quote) is { } trimmed && IntakeContentGuard.IsUnsafe(trimmed, material.Request) ? UnsafeQuote : Trimmed(quote);

    private static string Shorten(string? value)
    {
        var line = string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return line.Length <= 300 ? line : line[..299].TrimEnd() + "…";
    }
}

/// <summary>A part of the material with its kind; <see cref="FileName"/> is set for files, <see cref="Change"/> for a change of the person.</summary>
internal sealed record MaterialSlice(MaterialPart Part, string Text, string? FileName, FactChange? Change = null);

/// <summary>Helpers on <see cref="FactMaterial"/>.</summary>
internal static class FactMaterialExtensions
{
    /// <summary>
    /// Every part of the material in the order request, answers of the person, the changes of the person (their words only, as answers of
    /// the person, without their marks), answers of the assistant, profile, files, file map. A source marked <see cref="FactSource.LookedUp"/>
    /// is <see cref="MaterialPart.LookedUp"/>, never one of the person's files.
    /// </summary>
    public static IEnumerable<MaterialSlice> Parts(this FactMaterial material)
    {
        yield return new MaterialSlice(MaterialPart.Request, material.Request ?? string.Empty, null);
        foreach (var answer in material.PersonAnswers ?? [])
            yield return new MaterialSlice(MaterialPart.PersonAnswers, answer ?? string.Empty, null);
        foreach (var change in material.Changes ?? [])
        {
            if (change is not null)
                yield return new MaterialSlice(MaterialPart.PersonAnswers, change.Words ?? string.Empty, null, change);
        }
        foreach (var answer in material.AssistantAnswers ?? [])
            yield return new MaterialSlice(MaterialPart.AssistantAnswers, answer ?? string.Empty, null);
        if (!string.IsNullOrEmpty(material.Profile))
            yield return new MaterialSlice(MaterialPart.Profile, material.Profile, null);
        foreach (var file in material.Files ?? [])
            yield return new MaterialSlice(file.LookedUp ? MaterialPart.LookedUp : MaterialPart.Files, file.Text ?? string.Empty, file.FileName);
        if (!string.IsNullOrEmpty(material.FileMap))
            yield return new MaterialSlice(MaterialPart.Files, material.FileMap, null);
    }
}

/// <summary>Answer of the recheck for one finding.</summary>
public enum RecheckStatus { Fixed = 0, StillThere = 1, Unclear = 2 }

/// <summary>What the recheck said about the finding <see cref="Id"/>.</summary>
public sealed record RecheckResult(string Id, RecheckStatus Status, string? TextQuote);

/// <summary>Round 2 of the fact check: only the findings of round 1 are looked at again.</summary>
public static class FactRecheck
{
    /// <summary>
    /// Findings whose quote is still verbatim in <paramref name="draft"/> stay open without a model call; the rest must be asked.
    /// A source conflict concerns the files, which a correction does not change, so it always stays open without a call.
    /// </summary>
    public static (IReadOnlyList<FactFinding> StillThere, IReadOnlyList<FactFinding> ToAsk) Presort(IReadOnlyList<FactFinding> previous, string draft)
    {
        draft ??= string.Empty;
        var stillThere = new List<FactFinding>();
        var toAsk = new List<FactFinding>();
        foreach (var finding in previous ?? [])
        {
            if (finding.Kind == FactFindingKind.SourceConflict)
            {
                stillThere.Add(finding);
                continue;
            }
            if (string.IsNullOrWhiteSpace(finding.TextQuote))
            {
                toAsk.Add(finding);
                continue;
            }
            var match = QuoteVerifier.Find(draft, finding.TextQuote, allowApproximate: false);
            if (match.IsVerbatim)
            {
                var excerpt = QuoteVerifier.ExcerptWholeWords(draft, match);
                stillThere.Add(finding with { TextQuote = excerpt.Length > 0 ? excerpt : finding.TextQuote, Match = match.Kind });
            }
            else
            {
                toAsk.Add(finding);
            }
        }
        return (stillThere, toAsk);
    }

    /// <summary>Open findings after the recheck answer: still_there with a verified quote stays blocking, unverified or missing answers stay open but never block.</summary>
    public static IReadOnlyList<FactFinding> Resolve(IReadOnlyList<FactFinding> asked, IReadOnlyList<RecheckResult> results, string draft)
    {
        draft ??= string.Empty;
        var byId = new Dictionary<string, RecheckResult>(StringComparer.Ordinal);
        foreach (var result in results ?? [])
        {
            if (result?.Id is not null)
                byId.TryAdd(result.Id.Trim(), result);
        }

        var normalizedDraft = QuoteVerifier.Normalize(draft);
        var open = new List<FactFinding>();
        foreach (var finding in asked ?? [])
        {
            if (!byId.TryGetValue(finding.Id, out var result))
            {
                open.Add(Unclear(finding));
                continue;
            }
            switch (result.Status)
            {
                case RecheckStatus.Fixed:
                    continue;
                case RecheckStatus.StillThere when !string.IsNullOrWhiteSpace(result.TextQuote):
                    var match = QuoteVerifier.Find(draft, result.TextQuote.Trim(), allowApproximate: true);
                    if (match.IsFound && !FactFindingFilter.InPlaceholder(normalizedDraft, match.Start, match.Length))
                    {
                        var excerpt = QuoteVerifier.ExcerptWholeWords(draft, match);
                        open.Add(finding with { TextQuote = excerpt.Length > 0 ? excerpt : result.TextQuote.Trim(), Match = match.Kind });
                    }
                    else
                    {
                        open.Add(Unclear(finding));
                    }
                    break;
                default:
                    open.Add(Unclear(finding));
                    break;
            }
        }
        return open;
    }

    private static FactFinding Unclear(FactFinding finding) => finding with { Blocking = false, Match = QuoteMatchKind.NotFound };
}

/// <summary>
/// Keeps findings of earlier answered rounds of a run open in a later wording, so that a passage is never lost only because
/// a later check did not flag it again (checking models do not report the same findings on every run).
/// </summary>
public static class FactCarryOver
{
    /// <summary>
    /// <paramref name="fresh"/> (the findings of the check of <paramref name="draft"/>) plus every finding of
    /// <paramref name="earlier"/> (latest round first) that still applies: a passage finding whose quote still stands in
    /// <paramref name="draft"/> as whole words keeps its blocking effect, with the quote as it stands now; a source conflict
    /// always stays, because a correction of the text does not change the material (without a passage when its passage is gone).
    /// Only a repetition is dropped: a passage finding whose passage lies wholly inside the passage of one already kept of the same
    /// kind (for an instruction, also against the same instruction; for replaced information, also from the same side of the material,
    /// the pages looked up or not), which then blocks when either blocks (never when the kept one is against an instruction from before
    /// the latest change, <see cref="FactFinding.BeforeLastChange"/>), or a source conflict
    /// whose two passages contain each other as whole words and that names the same files. A passage that only overlaps a kept one
    /// is shown as well. No limit applies; ids are given anew (<c>f1…</c>).
    /// </summary>
    public static IReadOnlyList<FactFinding> Merge(IReadOnlyList<FactFinding> fresh, IEnumerable<IReadOnlyList<FactFinding>> earlier, string draft)
    {
        draft ??= string.Empty;
        var normalized = QuoteVerifier.Normalize(draft);
        (int Start, int Length)? RangeOf(FactFinding finding) => WordsOf(normalized,
            string.IsNullOrWhiteSpace(finding.TextQuote) ? null : QuoteVerifier.FindWholeWords(draft, finding.TextQuote));

        var kept = new List<(FactFinding Finding, (int Start, int Length)? Range)>();
        foreach (var finding in fresh ?? [])
        {
            if (finding is not null)
                kept.Add((finding, RangeOf(finding)));
        }

        foreach (var round in earlier ?? [])
        {
            foreach (var finding in round ?? [])
            {
                if (finding is null)
                    continue;
                if (finding.Kind == FactFindingKind.SourceConflict)
                {
                    if (kept.Any(k => k.Finding.Kind == FactFindingKind.SourceConflict && SameConflict(k.Finding, finding)))
                        continue;
                    var still = StillStanding(finding, draft);
                    kept.Add((still ?? finding with { TextQuote = null, Match = QuoteMatchKind.NotFound }, still is null ? null : RangeOf(still)));
                    continue;
                }

                if (StillStanding(finding, draft) is not { } carried || RangeOf(carried) is not { } range)
                    continue;
                var twin = kept.FindIndex(k => k.Finding.Kind == carried.Kind && k.Range is { } other
                    && other.Start <= range.Start && range.Start + range.Length <= other.Start + other.Length
                    && (carried.Kind != FactFindingKind.Instruction || SameInstruction(k.Finding, carried))
                    && (carried.Kind != FactFindingKind.Superseded || (k.Finding.MaterialFrom == MaterialPart.LookedUp) == (carried.MaterialFrom == MaterialPart.LookedUp)));
                if (twin >= 0)
                {
                    if (carried.Blocking && !kept[twin].Finding.Blocking && !kept[twin].Finding.BeforeLastChange)
                        kept[twin] = (kept[twin].Finding with { Blocking = true }, kept[twin].Range);
                    continue;
                }
                kept.Add((carried, range));
            }
        }

        return kept.Select((k, index) => k.Finding with { Id = $"f{index + 1}" }).ToList();
    }

    private static FactFinding? StillStanding(FactFinding finding, string draft)
    {
        if (string.IsNullOrWhiteSpace(finding.TextQuote) || !QuoteVerifier.ContainsWholeWords(draft, finding.TextQuote))
            return null;
        var match = QuoteVerifier.Find(draft, finding.TextQuote.Trim(), allowApproximate: false);
        var excerpt = match.IsVerbatim ? QuoteVerifier.ExcerptWholeWords(draft, match) : string.Empty;
        return excerpt.Length > 0 && QuoteVerifier.ContainsWholeWords(draft, excerpt)
            ? finding with { TextQuote = excerpt, Match = match.Kind }
            : finding with { Match = QuoteMatchKind.Normalized };
    }

    private static (int Start, int Length)? WordsOf(string normalized, (int Start, int Length)? range)
    {
        if (range is not { } r)
            return null;
        var (start, end) = (r.Start, Math.Min(normalized.Length, r.Start + r.Length));
        while (start < end && !char.IsLetterOrDigit(normalized[start]))
            start++;
        while (end > start && !char.IsLetterOrDigit(normalized[end - 1]))
            end--;
        return end > start ? (start, end - start) : r;
    }

    private static bool SameInstruction(FactFinding left, FactFinding right) =>
        left.Origin == right.Origin && string.Equals(Key(left.InstructionQuote), Key(right.InstructionQuote), StringComparison.Ordinal);

    private static bool SameConflict(FactFinding left, FactFinding right)
    {
        if (!Files(left).SetEquals(Files(right)))
            return false;
        return left.MaterialFrom == right.MaterialFrom && left.Material2From == right.Material2From
                && WithinEachOther(left.MaterialQuote, right.MaterialQuote) && WithinEachOther(left.MaterialQuote2, right.MaterialQuote2)
            || left.MaterialFrom == right.Material2From && left.Material2From == right.MaterialFrom
                && WithinEachOther(left.MaterialQuote, right.MaterialQuote2) && WithinEachOther(left.MaterialQuote2, right.MaterialQuote);
    }

    private static HashSet<string> Files(FactFinding conflict) =>
        new[] { conflict.MaterialFile, conflict.Material2File }.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f!.Trim()).ToHashSet(StringComparer.Ordinal);

    private static string Key(string? quote) => QuoteVerifier.Normalize(quote ?? string.Empty).Trim().ToLowerInvariant();

    private static bool WithinEachOther(string? left, string? right)
    {
        var (a, b) = (Key(left), Key(right));
        if (a.Length == 0 || b.Length == 0)
            return a.Length == 0 && b.Length == 0;
        return QuoteVerifier.ContainsWholeWords(a, b) || QuoteVerifier.ContainsWholeWords(b, a);
    }
}

/// <summary>
/// State of the fact check of the delivered version, as the person sees it. <see cref="VersionNotChecked"/>: the check ran on an
/// earlier wording, and the delivered text was changed afterwards without a check of its own.
/// </summary>
public enum FactCheckState { Off = 0, Checked = 1, OpenPoints = 2, NotCompleted = 3, NotShown = 4, VersionNotChecked = 5 }

/// <summary>
/// The fact check of the delivered version, for the report. <see cref="Corrected"/> are blocking findings of round 1 whose passage
/// the delivered version no longer has. <see cref="NotShown"/> counts passages the check flagged that could not be checked or
/// listed (over a limit of the answer or of the list); they are never counted as "nothing found".
/// </summary>
public sealed record FactCheckSummary(FactCheckMode Mode, FactCheckScope Scope, FactCheckState State, IReadOnlyList<FactFinding> Open,
    IReadOnlyList<FactFinding> SourceConflicts, IReadOnlyList<FactFinding> Corrected, int Calls, string? Model, bool UsedFallbackModel, string PromptVersion,
    int NotShown = 0);

/// <summary>Keys of <c>Finding.Metadata</c> written by the fact check; read by the writer's correction and the report.</summary>
public static class FactFindingMetadata
{
    public const string Code = "code", Id = "id", Kind = "kind", Quote = "quote", Instruction = "instruction", Material = "material", Material2 = "material2",
        MaterialFile = "materialFile", Origin = "origin", Blocking = "blocking", Mode = "mode", MaterialFrom = "materialFrom";
    public const string UnsupportedCode = "fact.unsupported", InstructionCode = "fact.instruction", SupersededCode = "fact.superseded", SourceConflictCode = "fact.source_conflict";

    /// <summary>Category of every finding of the fact check (<c>Finding.Category</c>, <c>Findings.Category</c>).</summary>
    public const string FindingCategory = "fact-check";
}
