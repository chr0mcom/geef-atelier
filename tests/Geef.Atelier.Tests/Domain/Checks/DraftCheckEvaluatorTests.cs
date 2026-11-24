using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Tests.Domain.Checks;

public sealed class DraftCheckEvaluatorTests
{
    private static string Words(int count) => string.Join(' ', Enumerable.Repeat("Wort", count));

    private static OrderConstraints With(LengthConstraint length) => OrderConstraints.None with { Length = length };

    private static DraftCheckResult Single(IReadOnlyList<DraftCheckResult> results, string code) =>
        Assert.Single(results, r => r.Code == code);

    [Fact]
    public void Empty_and_preamble_checks_always_run()
    {
        var results = DraftCheckEvaluator.Evaluate("Ein ganz normaler Text mit genug Wörtern darin.", OrderConstraints.None);

        Assert.True(Single(results, DraftCheckCodes.Empty).Passed);
        Assert.True(Single(results, DraftCheckCodes.Preamble).Passed);
        Assert.Equal(2, results.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Zu kurz.")]
    [InlineData("- - - # ##")]
    public void An_almost_empty_text_is_a_blocking_failure(string draft)
    {
        var result = Single(DraftCheckEvaluator.Evaluate(draft, OrderConstraints.None), DraftCheckCodes.Empty);

        Assert.False(result.Passed);
        Assert.True(result.Blocking);
    }

    [Theory]
    [InlineData("Here is the email you asked for:")]
    [InlineData("Hier ist der gewünschte Text:")]
    [InlineData("Gerne, hier ist die Mail:")]
    [InlineData("Sure! Here's your post:")]
    public void A_preamble_is_a_blocking_failure(string firstLine)
    {
        var result = Single(DraftCheckEvaluator.Evaluate(firstLine + "\n\nSehr geehrte Frau Muster, vielen Dank für Ihre Nachricht.", OrderConstraints.None), DraftCheckCodes.Preamble);

        Assert.False(result.Passed);
    }

    [Theory]
    [InlineData(LengthKind.AtMost, null, 200, 204, true)]
    [InlineData(LengthKind.AtMost, null, 200, 205, false)]
    [InlineData(LengthKind.AtLeast, 500, null, 490, true)]
    [InlineData(LengthKind.AtLeast, 500, null, 489, false)]
    [InlineData(LengthKind.Range, 120, 180, 114, true)]
    [InlineData(LengthKind.Range, 120, 180, 113, false)]
    [InlineData(LengthKind.Range, 120, 180, 189, true)]
    [InlineData(LengthKind.Range, 120, 180, 190, false)]
    [InlineData(LengthKind.About, 300, 300, 240, true)]
    [InlineData(LengthKind.About, 300, 300, 360, true)]
    [InlineData(LengthKind.About, 300, 300, 361, false)]
    [InlineData(LengthKind.Stated, 300, 300, 255, true)]
    [InlineData(LengthKind.Stated, 300, 300, 254, false)]
    [InlineData(LengthKind.Stated, 300, 300, 345, true)]
    [InlineData(LengthKind.Stated, 300, 300, 346, false)]
    public void Length_tolerances_follow_the_kind(LengthKind kind, int? min, int? max, int words, bool passes)
    {
        var result = Single(
            DraftCheckEvaluator.Evaluate(Words(words), With(new LengthConstraint(LengthUnit.Words, min, max, "quote", kind))),
            DraftCheckCodes.Length);

        Assert.Equal(passes, result.Passed);
        Assert.True(result.Blocking);
        Assert.Equal("quote", result.Quote);
    }

    [Fact]
    public void A_too_long_text_gets_a_message_that_names_the_count_the_request_and_the_fix()
    {
        var result = Single(
            DraftCheckEvaluator.Evaluate(Words(263), With(new LengthConstraint(LengthUnit.Words, null, 200, "max. 200 Wörter", LengthKind.AtMost))),
            DraftCheckCodes.Length);

        Assert.Equal(
            "The text has 263 words. The request asks for at most 200 words (\"max. 200 Wörter\"). Shorten it to at most 200 words without dropping anything the request requires.",
            result.Message);
    }

    [Fact]
    public void Markdown_symbols_are_not_counted_as_words()
    {
        Assert.Equal(3, DraftCheckEvaluator.CountWords("## Titel\n- eins\n- zwei"));
    }

    [Fact]
    public void Characters_include_spaces_but_not_line_breaks()
    {
        var result = Single(
            DraftCheckEvaluator.Evaluate("ab cd\nef gh ij kl mn", With(new LengthConstraint(LengthUnit.Characters, null, 19, "q", LengthKind.AtMost))),
            DraftCheckCodes.Length);

        Assert.True(result.Passed);
        Assert.Equal(19, DraftCheckEvaluator.CountCharacters("ab cd\nef gh ij kl mn"));
    }

    [Fact]
    public void The_language_is_checked_only_when_the_draft_language_is_certain()
    {
        var constraints = OrderConstraints.None with { Language = "de" };
        var english = "Thank you for your message. We are happy to confirm that the repair of the heating will take place on Monday, and we will be there at nine.";
        var german = "Vielen Dank für Ihre Nachricht. Wir bestätigen gerne, dass die Reparatur der Heizung am Montag stattfindet und wir um neun Uhr bei Ihnen sind.";

        Assert.False(Single(DraftCheckEvaluator.Evaluate(english, constraints), DraftCheckCodes.Language).Passed);
        Assert.True(Single(DraftCheckEvaluator.Evaluate(german, constraints), DraftCheckCodes.Language).Passed);
        Assert.DoesNotContain(DraftCheckEvaluator.Evaluate("Heizung Montag neun Uhr Termin", constraints), r => r.Code == DraftCheckCodes.Language);
    }

    [Fact]
    public void Placeholders_are_information_unless_the_request_forbids_them()
    {
        const string draft = "Sehr geehrte [Name der Ansprechperson], die Lieferung kommt am [Lieferdatum].";

        var allowed = Single(DraftCheckEvaluator.Evaluate(draft, OrderConstraints.None), DraftCheckCodes.Placeholders);
        Assert.True(allowed.Passed);
        Assert.False(allowed.Blocking);
        Assert.StartsWith("Fill in before sending:", allowed.Message);

        var forbidden = Single(
            DraftCheckEvaluator.Evaluate(draft, OrderConstraints.None with { ForbidPlaceholders = true, ForbidPlaceholdersQuote = "Keine Platzhalter" }),
            DraftCheckCodes.Placeholders);
        Assert.False(forbidden.Passed);
        Assert.True(forbidden.Blocking);
    }

    [Fact]
    public void Placeholder_search_skips_links_footnotes_and_check_boxes()
    {
        const string draft = "Siehe [die Webseite](https://example.org), Quelle [1], Fußnote [^2], Liste [ ] und [x], Bild ![Logo](logo.png), [12, 14]. Bitte [Datum] eintragen. [ref]: https://example.org";

        Assert.Equal(["[Datum]"], DraftCheckEvaluator.FindPlaceholders(draft));
    }

    [Fact]
    public void Forbidden_forms_are_found_line_by_line()
    {
        var constraints = OrderConstraints.None with
        {
            ForbiddenPatterns =
            [
                new PatternRule(ForbiddenPattern.Lists, "keine Aufzählungen"),
                new PatternRule(ForbiddenPattern.Headings, "keine Überschriften"),
                new PatternRule(ForbiddenPattern.Emojis, "keine Emojis"),
            ],
        };

        var clean = DraftCheckEvaluator.Evaluate("Ein Absatz ohne Liste. Im Jahr 2024 - so heißt es - war alles gut.", constraints);
        Assert.All(clean.Where(r => r.Code.StartsWith("pattern.", StringComparison.Ordinal)), r => Assert.True(r.Passed));

        var dirty = DraftCheckEvaluator.Evaluate("# Titel\n\n- erster Punkt\n1. zweiter Punkt\nSchön 🎉", constraints);
        Assert.All(dirty.Where(r => r.Code.StartsWith("pattern.", StringComparison.Ordinal)), r => Assert.False(r.Passed));
    }

    [Fact]
    public void Forbidden_terms_match_whole_words_and_required_terms_match_word_starts()
    {
        var constraints = OrderConstraints.None with
        {
            ForbiddenTerms = [new TermRule("Synergie", "q1")],
            RequiredTerms = [new TermRule("Kundenbindung", "q2")],
        };

        var results = DraftCheckEvaluator.Evaluate("Synergien entstehen durch Kundenbindungen, das zeigt die Praxis jeden Tag.", constraints);

        Assert.True(Single(results, DraftCheckCodes.ForbiddenTerm).Passed);
        Assert.True(Single(results, DraftCheckCodes.RequiredTerm).Passed);

        var violating = DraftCheckEvaluator.Evaluate("Die synergie ist groß, aber es fehlt etwas Wichtiges in diesem Text.", constraints);
        Assert.False(Single(violating, DraftCheckCodes.ForbiddenTerm).Passed);
        Assert.False(Single(violating, DraftCheckCodes.RequiredTerm).Passed);
    }
}
