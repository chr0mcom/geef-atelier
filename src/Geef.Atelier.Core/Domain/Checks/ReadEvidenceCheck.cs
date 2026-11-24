namespace Geef.Atelier.Core.Domain.Checks;

/// <summary>
/// How much of the attached files the writer most likely had in front of it, judged from the input tokens of its
/// answer. <see cref="ReadShare"/> is the actual input tokens divided by <see cref="ExpectedInputTokens"/>.
/// </summary>
public sealed record ReadEvidence(
    int SourceCharacters,
    double CharactersPerToken,
    int ExpectedInputTokens,
    int ActualInputTokens,
    double ReadShare,
    bool ReadInFull);

/// <summary>
/// Judges from the input tokens of the writer's answer whether the attached files were read in full. A CLI agent
/// that reads <c>context.md</c> completely carries the whole file in its last turn; one that only searches it
/// carries a small part. The expected value is a fixed agent overhead plus the file size in tokens. Only meaningful
/// for a writer in document mode that was asked to read everything; it is a hint, never a hard failure.
/// </summary>
public static class ReadEvidenceCheck
{
    /// <summary>Characters per token of German text (measured in the pre-test V1: 1.92–1.94).</summary>
    public const double GermanCharactersPerToken = 1.9;

    /// <summary>Characters per token of English text. An estimate, not measured yet; English needs fewer tokens per character.</summary>
    public const double EnglishCharactersPerToken = 3.5;

    /// <summary>Input tokens of the CLI agent without any file (tool list and instructions; measured in V1: 21,694).</summary>
    public const int AgentBaseTokens = 21_700;

    /// <summary>Below this share of the expected input tokens the files count as not read in full.</summary>
    public const double DefaultMinimumShare = 0.6;

    /// <summary>
    /// Characters per token for a source text: the English estimate when the text is certainly English, otherwise the
    /// measured German value (German is the common case and gives the stricter expectation).
    /// </summary>
    public static double CharactersPerTokenFor(string? sourceText) =>
        TextLanguageGuess.Guess(sourceText ?? string.Empty) == "en" ? EnglishCharactersPerToken : GermanCharactersPerToken;

    /// <summary>
    /// Compares <paramref name="actualInputTokens"/> with the tokens expected for <paramref name="sourceCharacters"/>.
    /// Returns <c>null</c> when there is nothing to judge (no files, or the provider reported no input tokens).
    /// </summary>
    public static ReadEvidence? Evaluate(
        int sourceCharacters,
        int actualInputTokens,
        double charactersPerToken = GermanCharactersPerToken,
        double minimumShare = DefaultMinimumShare)
    {
        if (sourceCharacters <= 0 || actualInputTokens <= 0 || charactersPerToken <= 0)
            return null;

        var expected = AgentBaseTokens + (int)Math.Round(sourceCharacters / charactersPerToken);
        var share = Math.Round((double)actualInputTokens / expected, 3);
        return new ReadEvidence(sourceCharacters, charactersPerToken, expected, actualInputTokens, share, share >= minimumShare);
    }
}
