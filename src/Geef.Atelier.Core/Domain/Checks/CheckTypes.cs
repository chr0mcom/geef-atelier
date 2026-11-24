namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>Unit of a length constraint.</summary>
public enum LengthUnit { Words = 0, Characters = 1 }

/// <summary>Forms the request can forbid. <see cref="Subheadings"/> forbids headings inside the text but allows a title on the first line.</summary>
public enum ForbiddenPattern { Lists = 0, Headings = 1, Emojis = 2, Subheadings = 3 }

/// <summary>How a length was phrased in the request; decides the tolerance of the check.</summary>
public enum LengthKind
{
    /// <summary>A range such as <c>120–180 Wörter</c>: accepted from Min·0.95 to Max·1.05.</summary>
    Range = 0,

    /// <summary>An upper limit such as <c>max. 200 Wörter</c>: accepted up to Max·1.02, rounded down.</summary>
    AtMost = 1,

    /// <summary>A lower limit such as <c>mindestens 500 Wörter</c>: accepted from Min·0.98, rounded up.</summary>
    AtLeast = 2,

    /// <summary>An approximate length such as <c>ca. 300 Wörter</c>: accepted within ±20 %.</summary>
    About = 3,

    /// <summary>A plain length such as <c>in 300 Wörtern</c>: accepted within ±15 %.</summary>
    Stated = 4,
}

/// <summary>
/// A length the request asks for; <see cref="Quote"/> is the passage of the request it was read from. <see cref="Min"/>
/// and <see cref="Max"/> are the numbers as stated (both equal for <see cref="LengthKind.About"/> and
/// <see cref="LengthKind.Stated"/>); the tolerance follows from <see cref="Kind"/>.
/// </summary>
public sealed record LengthConstraint(LengthUnit Unit, int? Min, int? Max, string Quote, LengthKind Kind = LengthKind.Range);

/// <summary>A form the request forbids, with the passage it was read from.</summary>
public sealed record PatternRule(ForbiddenPattern Pattern, string Quote);

/// <summary>A term the request forbids or requires, with the passage it was read from.</summary>
public sealed record TermRule(string Term, string Quote);

/// <summary>
/// Constraints read from the request without an AI model. Conservative: a constraint that is not certain is left out
/// or recorded in <see cref="Notes"/> instead.
/// </summary>
public sealed record OrderConstraints(
    LengthConstraint? Length,
    string? Language,
    bool ForbidPlaceholders,
    string? ForbidPlaceholdersQuote,
    IReadOnlyList<PatternRule> ForbiddenPatterns,
    IReadOnlyList<TermRule> ForbiddenTerms,
    IReadOnlyList<TermRule> RequiredTerms,
    IReadOnlyList<string> Notes)
{
    /// <summary>No constraints at all.</summary>
    public static OrderConstraints None { get; } = new(null, null, false, null, [], [], [], []);
}

/// <summary>
/// Result of one check of a draft. <see cref="Blocking"/> results that did not pass lead to a correction; results that
/// did not pass and are not blocking (a possible remark to the reader) lead to none but are shown as open points.
/// </summary>
public sealed record DraftCheckResult(string Code, bool Passed, bool Blocking, string Message, string? Quote);

/// <summary>Known codes of <see cref="DraftCheckResult"/>.</summary>
public static class DraftCheckCodes
{
    public const string Empty = "empty";
    public const string Preamble = "preamble";
    public const string Length = "length";
    public const string Language = "language";
    public const string Placeholders = "placeholders";
    public const string Lists = "pattern.lists";
    public const string Headings = "pattern.headings";
    public const string Emojis = "pattern.emojis";
    public const string ForbiddenTerm = "term.forbidden";
    public const string RequiredTerm = "term.required";
}
