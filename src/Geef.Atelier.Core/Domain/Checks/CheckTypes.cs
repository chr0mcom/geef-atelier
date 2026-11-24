namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>Unit of a length constraint.</summary>
public enum LengthUnit { Words = 0, Characters = 1 }

/// <summary>Forms the request can forbid.</summary>
public enum ForbiddenPattern { Lists = 0, Headings = 1, Emojis = 2 }

/// <summary>A length the request asks for; <see cref="Quote"/> is the passage of the request it was read from.</summary>
public sealed record LengthConstraint(LengthUnit Unit, int? Min, int? Max, string Quote);

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

/// <summary>Result of one check of a draft. <see cref="Blocking"/> results that did not pass lead to a correction.</summary>
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
