using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Tests.Domain.Checks;

/// <summary>
/// Fixes the structure of how lengths are read next to negations: a negation only acts inside its own clause, a
/// wording with one direction is never turned around, "more than" is turned only by a negation right before it, and an
/// unclear length never becomes a blocking check.
/// </summary>
public sealed class RequestConstraintNegationTests
{
    public sealed record Base(string Sentence, LengthKind Kind, int Number, bool German);

    private static readonly Base[] UnambiguousBases =
    [
        new("Schreibe höchstens 150 Wörter", LengthKind.AtMost, 150, true),
        new("Schreibe maximal 150 Wörter", LengthKind.AtMost, 150, true),
        new("Schreibe bis zu 150 Wörter", LengthKind.AtMost, 150, true),
        new("Schreibe weniger als 150 Wörter", LengthKind.AtMost, 150, true),
        new("Schreibe nicht mehr als 150 Wörter", LengthKind.AtMost, 150, true),
        new("Die Antwort soll unter 150 Wörtern bleiben", LengthKind.AtMost, 150, true),
        new("Halte dich an max. 150 Wörter", LengthKind.AtMost, 150, true),
        new("Die Mail darf höchstens 150 Wörter haben", LengthKind.AtMost, 150, true),
        new("Schreibe 150 Wörter oder weniger", LengthKind.AtMost, 150, true),
        new("Write at most 150 words", LengthKind.AtMost, 150, false),
        new("Write no more than 150 words", LengthKind.AtMost, 150, false),
        new("Write fewer than 150 words", LengthKind.AtMost, 150, false),
        new("Write less than 150 words", LengthKind.AtMost, 150, false),
        new("Keep it under 150 words", LengthKind.AtMost, 150, false),
        new("Write up to 150 words", LengthKind.AtMost, 150, false),
        new("Write not more than 150 words", LengthKind.AtMost, 150, false),
        new("Use a maximum of 150 words", LengthKind.AtMost, 150, false),
        new("The reply must be 150 words or less", LengthKind.AtMost, 150, false),
        new("Schreibe mindestens 400 Wörter", LengthKind.AtLeast, 400, true),
        new("Schreibe mehr als 400 Wörter", LengthKind.AtLeast, 400, true),
        new("Schreibe nicht weniger als 400 Wörter", LengthKind.AtLeast, 400, true),
        new("Schreibe wenigstens 400 Wörter", LengthKind.AtLeast, 400, true),
        new("Der Text soll mind. 400 Wörter haben", LengthKind.AtLeast, 400, true),
        new("Der Beitrag soll mehr als 400 Wörter haben", LengthKind.AtLeast, 400, true),
        new("Schreibe 400 Wörter oder mehr", LengthKind.AtLeast, 400, true),
        new("Write at least 400 words", LengthKind.AtLeast, 400, false),
        new("Write more than 400 words", LengthKind.AtLeast, 400, false),
        new("Write no fewer than 400 words", LengthKind.AtLeast, 400, false),
        new("Write no less than 400 words", LengthKind.AtLeast, 400, false),
        new("Write a minimum of 400 words", LengthKind.AtLeast, 400, false),
        new("Write 400 words or more", LengthKind.AtLeast, 400, false),
        new("The essay should exceed 400 words", LengthKind.AtLeast, 400, false),
    ];

    private static readonly string[] GermanNegatedAddOns =
    [
        "keine Floskeln",
        "nicht zu förmlich",
        "verwende keine Fachbegriffe",
        "ohne Zwischenüberschriften",
        "nie im Passiv",
        "auf keinen Fall Anglizismen",
        "schreibe nicht im Konjunktiv",
        "keinesfalls langweilig",
        "kein Fachjargon",
    ];

    private static readonly string[] EnglishNegatedAddOns =
    [
        "don't use emojis",
        "not too formal",
        "that isn't boring",
        "never use jargon",
        "without bullet points",
        "no clichés",
        "do not mention prices",
        "it shouldn't sound like an ad",
        "avoid anything that isn't relevant",
    ];

    private static readonly string[] GermanConjunctions = ["und", "aber"];
    private static readonly string[] EnglishConjunctions = ["and", "but"];

    public static TheoryData<string, LengthKind, int> SentencesWithNegatedAddOns()
    {
        var data = new TheoryData<string, LengthKind, int>();
        foreach (var basis in UnambiguousBases)
        {
            var addOns = basis.German ? GermanNegatedAddOns : EnglishNegatedAddOns;
            var conjunctions = basis.German ? GermanConjunctions : EnglishConjunctions;
            foreach (var addOn in addOns)
            {
                foreach (var conjunction in conjunctions)
                {
                    data.Add($"{basis.Sentence} {conjunction} {addOn}.", basis.Kind, basis.Number);
                    data.Add($"{Capitalize(addOn)} {conjunction} {Decapitalize(basis.Sentence)}.", basis.Kind, basis.Number);
                }
            }
        }
        return data;
    }

    [Fact]
    public void The_lists_for_the_clause_property_are_large_enough()
    {
        Assert.True(UnambiguousBases.Length >= 25);
        Assert.True(GermanNegatedAddOns.Length + EnglishNegatedAddOns.Length >= 15);
        Assert.True(SentencesWithNegatedAddOns().Count() >= 700);
    }

    [Theory]
    [MemberData(nameof(BaseSentences))]
    public void Every_base_wording_is_read_on_its_own(string sentence, LengthKind kind, int number)
    {
        AssertLimit(RequestConstraintExtractor.Extract(sentence + ".").Length, kind, number);
    }

    public static TheoryData<string, LengthKind, int> BaseSentences()
    {
        var data = new TheoryData<string, LengthKind, int>();
        foreach (var basis in UnambiguousBases)
            data.Add(basis.Sentence, basis.Kind, basis.Number);
        return data;
    }

    [Theory]
    [MemberData(nameof(SentencesWithNegatedAddOns))]
    public void A_negation_in_another_clause_never_changes_the_limit(string request, LengthKind kind, int number)
    {
        AssertLimit(RequestConstraintExtractor.Extract(request).Length, kind, number);
    }

    private static readonly string[] TurningPrefixesGerman = ["Der Text soll auf keinen Fall", "Der Text soll nie", "Der Text darf niemals", "Schreibe nicht", "Schreibe nie"];
    private static readonly string[] TurningPrefixesEnglish = ["The text must not be", "It should never be", "Don't write", "Do not use", "Never write", "Please don't use"];

    public static TheoryData<string> AmbiguousWordingsRightAfterANegation()
    {
        var data = new TheoryData<string>();
        foreach (var prefix in TurningPrefixesGerman)
            data.Add($"{prefix} mehr als 200 Wörter.");
        foreach (var prefix in TurningPrefixesEnglish)
        {
            data.Add($"{prefix} more than 200 words.");
            data.Add($"{prefix} more than 200 words and don't use emojis.");
        }
        data.Add("The reply must not exceed 200 words.");
        data.Add("Don't exceed 200 words.");
        data.Add("Mehr als 200 Wörter sollen es nicht sein.");
        data.Add("Mehr als 200 Wörter dürfen es nicht sein.");
        return data;
    }

    [Theory]
    [MemberData(nameof(AmbiguousWordingsRightAfterANegation))]
    public void A_negation_right_before_more_than_turns_it_into_an_upper_limit(string request)
    {
        AssertLimit(RequestConstraintExtractor.Extract(request).Length, LengthKind.AtMost, 200);
    }

    public static TheoryData<string, LengthKind> UnambiguousWordingsAfterANegation()
    {
        string[] german = ["höchstens", "maximal", "bis zu", "weniger als", "unter", "mindestens", "wenigstens"];
        string[] english = ["at most", "up to", "less than", "fewer than", "under", "no more than", "at least", "no fewer than"];
        string[] englishPrefixes = ["The text must not be", "It should never be", "Don't write", "Do not use", "Please don't use"];
        string[] germanPrefixes = ["Der Text soll auf keinen Fall", "Der Text darf niemals", "Der Text darf nie", "Schreibe ohne Floskeln"];
        var data = new TheoryData<string, LengthKind>();
        foreach (var prefix in germanPrefixes)
            foreach (var wording in german)
                data.Add($"{prefix} {wording} 200 Wörter.", KindOf(wording));
        foreach (var prefix in englishPrefixes)
            foreach (var wording in english)
                data.Add($"{prefix} {wording} 200 words.", KindOf(wording));
        return data;
    }

    [Theory]
    [MemberData(nameof(UnambiguousWordingsAfterANegation))]
    public void A_negation_never_turns_a_wording_with_one_direction(string request, LengthKind statedKind)
    {
        var constraints = RequestConstraintExtractor.Extract(request);

        Assert.False(constraints.Length is { } length && length.Kind != statedKind,
            $"\"{request}\" was turned into {constraints.Length?.Kind}.");
        Assert.Null(constraints.Length);
        Assert.Contains(constraints.Notes, n => n.StartsWith("A length next to a negation", StringComparison.Ordinal));
    }

    public static TheoryData<string> LowerAndUpperPairs()
    {
        string[] germanLower = ["mehr als", "mindestens", "nicht weniger als"];
        string[] germanUpper = ["nicht mehr als", "höchstens", "weniger als", "maximal", "bis zu"];
        string[] germanJoin = ["und", "aber", ", aber"];
        string[] englishLower = ["more than", "at least", "no fewer than", "no less than"];
        string[] englishUpper = ["not more than", "no more than", "at most", "less than", "fewer than", "up to"];
        string[] englishJoin = ["and", "but", ", but"];
        var data = new TheoryData<string>();
        foreach (var lower in germanLower)
            foreach (var upper in germanUpper)
                foreach (var join in germanJoin)
                {
                    data.Add($"Schreibe {lower} 120 Wörter{Space(join)}{join} {upper} 250 Wörter.");
                    data.Add($"Schreibe {lower} 120{Space(join)}{join} {upper} 250 Wörter.");
                }
        foreach (var lower in englishLower)
            foreach (var upper in englishUpper)
                foreach (var join in englishJoin)
                {
                    data.Add($"Write {lower} 120 words{Space(join)}{join} {upper} 250 words.");
                    data.Add($"Write {lower} 120{Space(join)}{join} {upper} 250 words.");
                }
        return data;
    }

    [Theory]
    [MemberData(nameof(LowerAndUpperPairs))]
    public void A_lower_and_an_upper_limit_joined_by_a_conjunction_give_the_pair(string request)
    {
        var length = RequestConstraintExtractor.Extract(request).Length;

        Assert.NotNull(length);
        Assert.Equal(LengthKind.Range, length.Kind);
        Assert.Equal(120, length.Min);
        Assert.Equal(250, length.Max);
    }

    [Theory]
    [InlineData("Write a blog post of more than 300 words that isn't boring.")]
    [InlineData("Write more than 300 words about why it's not worth it.")]
    [InlineData("Ich will keinen Text mit mehr als 300 Wörtern.")]
    [InlineData("Don't make the text more than 300 words long.")]
    [InlineData("More than 300 words is not what I want.")]
    [InlineData("Ich möchte nicht, dass der Text mehr als 300 Wörter hat.")]
    [InlineData("I don't want a text, which has more than 300 words.")]
    [InlineData("Mehr als 300 Wörter, das will ich nicht.")]
    [InlineData("Write less than 200 words and don't write less than 100 words.")]
    [InlineData("Schreibe ohne Floskeln höchstens 100 Wörter.")]
    [InlineData("Höchstens 100 Wörter sollen es nicht sein.")]
    [InlineData("Never write more than 100 words that are not useful.")]
    [InlineData("Write it not in 300 words.")]
    [InlineData("Schreibe mehr als 300 Wörter auf 2 Seiten.")]
    [InlineData("Eine Rede von 5 Minuten mit höchstens 700 Wörtern.")]
    [InlineData("Schreibe mehr als 200 und weniger als 100 Wörter.")]
    [InlineData("Schreibe mindestens 100 Wörter und höchstens 900 Zeichen.")]
    [InlineData("Write at most 200 words. Ich will keinen Text mit mehr als 300 Wörtern.")]
    public void An_unclear_length_never_becomes_a_check(string request)
    {
        var constraints = RequestConstraintExtractor.Extract(request);

        Assert.Null(constraints.Length);
        Assert.NotEmpty(constraints.Notes);
        Assert.DoesNotContain(DraftCheckEvaluator.Evaluate(Words(500), constraints), r => r.Code == DraftCheckCodes.Length);
    }

    [Theory]
    [InlineData("Schreibe mehr als 100 Wörter und nicht mehr als 200 Wörter.", "Range", 100, 200)]
    [InlineData("Write more than 100 words but not more than 200 words.", "Range", 100, 200)]
    [InlineData("Write less than 100 words and don't use bullet points.", "AtMost", null, 100)]
    [InlineData("Write less than 100 words and never use jargon.", "AtMost", null, 100)]
    [InlineData("Schreibe weniger als 100 Wörter und nicht zu förmlich.", "AtMost", null, 100)]
    [InlineData("Der Text soll mehr als 100 Wörter haben und nicht zu lang sein.", "AtLeast", 100, null)]
    [InlineData("Write more than 100 words and don't be boring.", "AtLeast", 100, null)]
    [InlineData("Write less than 200 words and don't write less than 100 words.", "none", null, null)]
    [InlineData("Write more than 100 words and don't use emojis.", "AtLeast", 100, null)]
    [InlineData("Write more than 100 words and not too formal.", "AtLeast", 100, null)]
    [InlineData("Write a blog post of more than 300 words that isn't boring.", "none", null, null)]
    [InlineData("Schreibe mehr als 200 Wörter und nicht zu förmlich.", "AtLeast", 200, null)]
    [InlineData("Write more than 300 words about why it's not worth it.", "none", null, null)]
    [InlineData("Write less than 100 words and don't be boring.", "AtMost", null, 100)]
    [InlineData("Write a post of fewer than 100 words that doesn't use jargon.", "AtMost", null, 100)]
    [InlineData("Bitte keine Floskeln und weniger als 100 Wörter.", "AtMost", null, 100)]
    [InlineData("Schreibe keine Floskeln und mehr als 100 Wörter.", "AtLeast", 100, null)]
    public void Golden_table_of_the_second_review(string request, string expected, int? min, int? max)
    {
        var length = RequestConstraintExtractor.Extract(request).Length;

        if (expected == "none")
        {
            Assert.Null(length);
            return;
        }
        Assert.NotNull(length);
        Assert.Equal(Enum.Parse<LengthKind>(expected), length.Kind);
        Assert.Equal(min, length.Min);
        Assert.Equal(max, length.Max);
    }

    [Theory]
    [InlineData("Don't write more than 100 words.", "Don't write more than 100 words")]
    [InlineData("Don’t write more than 120 words.", "Don’t write more than 120 words")]
    [InlineData("It shouldn't be more than 100 words.", "shouldn't be more than 100 words")]
    [InlineData("Please do not use more than 200 words for the reply.", "not use more than 200 words")]
    [InlineData("Mehr als 100 Wörter sollen es nicht sein.", "Mehr als 100 Wörter sollen es nicht sein")]
    public void A_turned_limit_quotes_the_whole_negation(string request, string quote)
    {
        Assert.Equal(quote, RequestConstraintExtractor.Extract(request).Length!.Quote);
    }

    [Theory]
    [InlineData("Ich will keinen kurzen Text, schreibe mehr als 300 Wörter.", 300)]
    [InlineData("Don't make it boring and write more than 300 words.", 300)]
    [InlineData("Schreibe einen Beitrag, der nicht langweilig ist, mit mehr als 500 Wörtern.", 500)]
    [InlineData("Nicht kurz, sondern mehr als 300 Wörter.", 300)]
    [InlineData("Keine Einleitung, mindestens 300 Wörter.", 300)]
    public void A_negated_clause_next_to_the_length_leaves_it_alone(string request, int min)
    {
        AssertLimit(RequestConstraintExtractor.Extract(request).Length, LengthKind.AtLeast, min);
    }

    [Theory]
    [InlineData("Die Einleitung soll kurz sein und höchstens 100 Wörter haben.")]
    [InlineData("Die Einleitung soll kurz sein, höchstens 100 Wörter.")]
    public void A_part_of_the_text_still_counts_across_clauses(string request)
    {
        Assert.Null(RequestConstraintExtractor.Extract(request).Length);
    }

    private static void AssertLimit(LengthConstraint? length, LengthKind kind, int number)
    {
        Assert.NotNull(length);
        Assert.Equal(kind, length.Kind);
        if (kind == LengthKind.AtMost)
        {
            Assert.Null(length.Min);
            Assert.Equal(number, length.Max);
        }
        else
        {
            Assert.Equal(number, length.Min);
            Assert.Null(length.Max);
        }
    }

    private static LengthKind KindOf(string wording) =>
        wording is "mindestens" or "wenigstens" or "at least" or "no fewer than" ? LengthKind.AtLeast : LengthKind.AtMost;

    private static string Space(string join) => join.StartsWith(',') ? string.Empty : " ";

    private static string Capitalize(string value) => char.ToUpperInvariant(value[0]) + value[1..];

    private static string Decapitalize(string value) => char.ToLowerInvariant(value[0]) + value[1..];

    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("Wort", count));
}
