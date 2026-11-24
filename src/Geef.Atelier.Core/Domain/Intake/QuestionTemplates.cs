using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Core.Domain.Intake;

/// <summary>An option of the length template: its fixed id, the label and the approximate word count it stands for.</summary>
public sealed record LengthTemplateOption(string Id, string Label, int Words);

/// <summary>The fixed length question in German and English; the model never words it.</summary>
public static class QuestionTemplates
{
    /// <summary>Id of the length question.</summary>
    public const string LengthQuestionId = "length";

    /// <summary>Id of the shortest length option.</summary>
    public const string ShortOptionId = "short";

    /// <summary>Id of the middle length option.</summary>
    public const string MediumOptionId = "medium";

    /// <summary>Id of the longest length option.</summary>
    public const string LongOptionId = "long";

    /// <summary>The German length question.</summary>
    public const string GermanLengthQuestion = "Wie lang soll der Text ungefähr werden?";

    /// <summary>The English length question.</summary>
    public const string EnglishLengthQuestion = "About how long should the text be?";

    /// <summary>Question text per language code.</summary>
    public static IReadOnlyDictionary<string, string> LengthQuestions { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["de"] = GermanLengthQuestion,
        ["en"] = EnglishLengthQuestion,
    };

    /// <summary>Options per language code and length scale, shortest first.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<LengthScale, IReadOnlyList<LengthTemplateOption>>> LengthOptions { get; } =
        new Dictionary<string, IReadOnlyDictionary<LengthScale, IReadOnlyList<LengthTemplateOption>>>(StringComparer.Ordinal)
        {
            ["de"] = new Dictionary<LengthScale, IReadOnlyList<LengthTemplateOption>>
            {
                [LengthScale.ShortForm] =
                [
                    new(ShortOptionId, "Sehr kurz (etwa 50 Wörter)", 50),
                    new(MediumOptionId, "Kurz (etwa 120 Wörter)", 120),
                    new(LongOptionId, "Etwas länger (etwa 250 Wörter)", 250),
                ],
                [LengthScale.Standard] =
                [
                    new(ShortOptionId, "Kurz (etwa 150 Wörter)", 150),
                    new(MediumOptionId, "Mittel (etwa 400 Wörter)", 400),
                    new(LongOptionId, "Lang (etwa 800 Wörter)", 800),
                ],
                [LengthScale.LongForm] =
                [
                    new(ShortOptionId, "Kompakt (etwa 800 Wörter)", 800),
                    new(MediumOptionId, "Mittel (etwa 1.500 Wörter)", 1_500),
                    new(LongOptionId, "Ausführlich (etwa 3.000 Wörter)", 3_000),
                ],
            },
            ["en"] = new Dictionary<LengthScale, IReadOnlyList<LengthTemplateOption>>
            {
                [LengthScale.ShortForm] =
                [
                    new(ShortOptionId, "Very short (about 50 words)", 50),
                    new(MediumOptionId, "Short (about 120 words)", 120),
                    new(LongOptionId, "A bit longer (about 250 words)", 250),
                ],
                [LengthScale.Standard] =
                [
                    new(ShortOptionId, "Short (about 150 words)", 150),
                    new(MediumOptionId, "Medium (about 400 words)", 400),
                    new(LongOptionId, "Long (about 800 words)", 800),
                ],
                [LengthScale.LongForm] =
                [
                    new(ShortOptionId, "Compact (about 800 words)", 800),
                    new(MediumOptionId, "Medium (about 1,500 words)", 1_500),
                    new(LongOptionId, "Detailed (about 3,000 words)", 3_000),
                ],
            },
        };

    /// <summary>True when a length template exists for <paramref name="language"/>.</summary>
    public static bool Supports(string? language) => throw new NotImplementedException();

    /// <summary>The length question for <paramref name="language"/> and <paramref name="scale"/>.</summary>
    public static ClarifyQuestion Length(string language, LengthScale scale) => throw new NotImplementedException();

    /// <summary>The length constraint of an answered length question: the chosen option as an approximate word count; null for free text, "decide for me" or an unknown option.</summary>
    public static LengthConstraint? ToLengthConstraint(OrderAnswer answer, string language, LengthScale scale) => throw new NotImplementedException();
}
