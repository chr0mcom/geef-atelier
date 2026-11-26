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
    [InlineData("   \n  ")]
    [InlineData("- - - # ##")]
    [InlineData("… ?! –")]
    public void A_text_without_a_single_word_is_a_blocking_failure(string draft)
    {
        var result = Single(DraftCheckEvaluator.Evaluate(draft, OrderConstraints.None), DraftCheckCodes.Empty);

        Assert.False(result.Passed);
        Assert.True(result.Blocking);
    }

    [Theory]
    [InlineData("Frisch geröstet, täglich genossen")]
    [InlineData("Re: Ihr Angebot vom Montag")]
    [InlineData("Atelier")]
    [InlineData("Zu kurz.")]
    public void A_short_text_is_not_empty(string draft)
    {
        var results = DraftCheckEvaluator.Evaluate(draft, OrderConstraints.None);

        Assert.True(Single(results, DraftCheckCodes.Empty).Passed);
        Assert.DoesNotContain(results, r => r.Blocking && !r.Passed);
    }

    [Fact]
    public void A_four_word_slogan_passes_every_check_of_its_request()
    {
        var constraints = RequestConstraintExtractor.Extract("Schreibe mir einen Slogan für mein Café.");

        var results = DraftCheckEvaluator.Evaluate("Frisch geröstet, täglich genossen", constraints);

        Assert.DoesNotContain(results, r => !r.Passed);
    }

    [Fact]
    public void A_stated_lower_limit_still_fails_a_short_text()
    {
        var constraints = RequestConstraintExtractor.Extract("Schreibe mindestens 50 Wörter über unser Café.");

        var results = DraftCheckEvaluator.Evaluate(Words(10), constraints);

        var length = Single(results, DraftCheckCodes.Length);
        Assert.False(length.Passed);
        Assert.True(length.Blocking);
        Assert.True(Single(results, DraftCheckCodes.Empty).Passed);
    }

    [Theory]
    [InlineData("Here is the email you asked for:")]
    [InlineData("Hier ist der gewünschte Text:")]
    [InlineData("Gerne, hier ist die Mail:")]
    [InlineData("Sure! Here's your post:")]
    [InlineData("Here's what I learned from ten years in sales:")]
    [InlineData("Gerne teile ich heute drei Erkenntnisse aus meinem ersten Jahr:")]
    public void A_possible_preamble_is_an_open_point_that_never_blocks(string firstLine)
    {
        var result = Single(DraftCheckEvaluator.Evaluate(firstLine + "\n\nSehr geehrte Frau Muster, vielen Dank für Ihre Nachricht.", OrderConstraints.None), DraftCheckCodes.Preamble);

        Assert.False(result.Passed);
        Assert.False(result.Blocking);
        Assert.StartsWith("The text may start with a remark to the reader", result.Message);
    }

    [Fact]
    public void A_text_that_starts_directly_has_no_preamble_note()
    {
        var result = Single(DraftCheckEvaluator.Evaluate("Sehr geehrte Frau Muster, vielen Dank für Ihre Nachricht.", OrderConstraints.None), DraftCheckCodes.Preamble);

        Assert.True(result.Passed);
        Assert.True(result.Blocking);
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
    [InlineData(LengthKind.AtMost, null, 5, 5, true)]
    [InlineData(LengthKind.AtMost, null, 5, 6, false)]
    [InlineData(LengthKind.AtMost, null, 6, 7, false)]
    [InlineData(LengthKind.AtMost, null, 49, 49, true)]
    [InlineData(LengthKind.AtMost, null, 49, 50, false)]
    [InlineData(LengthKind.AtMost, null, 50, 51, true)]
    [InlineData(LengthKind.AtLeast, 20, null, 20, true)]
    [InlineData(LengthKind.AtLeast, 20, null, 19, false)]
    [InlineData(LengthKind.AtLeast, 100, null, 98, true)]
    [InlineData(LengthKind.AtLeast, 100, null, 97, false)]
    [InlineData(LengthKind.Range, 10, 20, 9, false)]
    [InlineData(LengthKind.Range, 10, 20, 10, true)]
    [InlineData(LengthKind.Range, 10, 20, 21, true)]
    [InlineData(LengthKind.Range, 10, 20, 22, false)]
    public void Length_tolerances_follow_the_kind(LengthKind kind, int? min, int? max, int words, bool passes)
    {
        var result = Single(
            DraftCheckEvaluator.Evaluate(Words(words), With(new LengthConstraint(LengthUnit.Words, min, max, "quote", kind))),
            DraftCheckCodes.Length);

        Assert.Equal(passes, result.Passed);
        Assert.Equal(passes, result.Blocking);
        Assert.Equal("quote", result.Quote);
    }

    [Theory]
    [InlineData(LengthKind.AtMost, null, 100, 125, false)]
    [InlineData(LengthKind.AtMost, null, 100, 126, true)]
    [InlineData(LengthKind.AtLeast, 200, null, 150, false)]
    [InlineData(LengthKind.AtLeast, 200, null, 149, true)]
    [InlineData(LengthKind.Range, 120, 180, 90, false)]
    [InlineData(LengthKind.Range, 120, 180, 89, true)]
    [InlineData(LengthKind.Range, 120, 180, 225, false)]
    [InlineData(LengthKind.Range, 120, 180, 226, true)]
    [InlineData(LengthKind.About, 300, 300, 225, false)]
    [InlineData(LengthKind.About, 300, 300, 224, true)]
    [InlineData(LengthKind.About, 300, 300, 375, false)]
    [InlineData(LengthKind.About, 300, 300, 376, true)]
    [InlineData(LengthKind.Stated, 300, 300, 225, false)]
    [InlineData(LengthKind.Stated, 300, 300, 376, true)]
    public void Only_a_miss_of_more_than_a_quarter_of_the_limit_asks_for_a_correction(LengthKind kind, int? min, int? max, int words, bool blocking)
    {
        var result = Single(
            DraftCheckEvaluator.Evaluate(Words(words), With(new LengthConstraint(LengthUnit.Words, min, max, "quote", kind))),
            DraftCheckCodes.Length);

        Assert.False(result.Passed);
        Assert.Equal(blocking, result.Blocking);
        if (!blocking)
            Assert.Contains("The difference is small", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_small_miss_in_characters_is_only_an_open_point()
    {
        var result = Single(
            DraftCheckEvaluator.Evaluate(new string('a', 120), With(new LengthConstraint(LengthUnit.Characters, null, 100, "q", LengthKind.AtMost))),
            DraftCheckCodes.Length);

        Assert.False(result.Passed);
        Assert.False(result.Blocking);
    }

    [Fact]
    public void A_length_miss_next_to_another_blocking_problem_does_not_ask_to_keep_everything_else()
    {
        var constraints = OrderConstraints.None with
        {
            Length = new LengthConstraint(LengthUnit.Words, null, 10, "max. 10 Wörter", LengthKind.AtMost),
            ForbiddenTerms = [new TermRule("Wort", "Vermeide „Wort“")],
        };

        var length = Single(DraftCheckEvaluator.Evaluate(Words(40), constraints), DraftCheckCodes.Length);

        Assert.False(length.Passed);
        Assert.True(length.Blocking);
        Assert.DoesNotContain(DraftCheckEvaluator.KeepEverythingElse, length.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_too_long_text_gets_a_message_that_names_the_count_the_request_and_the_fix()
    {
        var result = Single(
            DraftCheckEvaluator.Evaluate(Words(263), With(new LengthConstraint(LengthUnit.Words, null, 200, "max. 200 Wörter", LengthKind.AtMost))),
            DraftCheckCodes.Length);

        Assert.Equal(
            "The text has 263 words. The request asks for at most 200 words (\"max. 200 Wörter\"). Shorten it to at most 200 words without dropping anything the request requires. Keep everything else unchanged.",
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

    private const string LongPlaceholder = "[Art der Rückmeldung, z. B. über den Abschnitt unten, per E-Mail oder über das Hausaufgabenheft]";

    [Fact]
    public void A_placeholder_of_94_characters_is_found_and_listed_to_fill_in()
    {
        var draft = $"Bitte geben Sie uns bis zum [Datum] Bescheid ({LongPlaceholder}), ob Sie kommen.";

        Assert.Equal(94, LongPlaceholder.Length - 2);
        Assert.Equal(["[Datum]", LongPlaceholder], DraftCheckEvaluator.FindPlaceholders(draft));
        var check = Single(DraftCheckEvaluator.Evaluate(draft, OrderConstraints.None), DraftCheckCodes.Placeholders);
        Assert.Contains(LongPlaceholder, check.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Placeholders_are_found_up_to_300_characters_on_one_line()
    {
        var longest = "[" + new string('a', DraftCheckEvaluator.MaxPlaceholderChars) + "]";
        var tooLong = "[" + new string('b', DraftCheckEvaluator.MaxPlaceholderChars + 1) + "]";
        var twoLines = "[Name der Lehrkraft\nund Klasse]";

        Assert.Equal(300, DraftCheckEvaluator.MaxPlaceholderChars);
        Assert.Equal([longest], DraftCheckEvaluator.FindPlaceholders($"Text {longest} Text {tooLong} Text {twoLines}"));
    }

    [Fact]
    public void Long_brackets_that_are_links_references_images_footnotes_or_check_boxes_are_still_no_placeholders()
    {
        var words = string.Join(' ', Enumerable.Repeat("Beschluss der Mitgliederversammlung", 5));
        var draft = string.Join('\n',
            $"Siehe [{words}](https://example.org/protokoll).",
            $"Wie im [{words}][1] beschrieben.",
            $"[{words}]: https://example.org/verweis",
            $"Bild ![{words}](bild.png)",
            $"Fußnote [^{words}]",
            $"Doppelt [[{words}]]",
            "Bitte [Datum] eintragen.");

        Assert.True(words.Length > 80);
        Assert.Equal(["[Datum]"], DraftCheckEvaluator.FindPlaceholders(draft));
    }

    [Fact]
    public void A_long_placeholder_blocks_when_the_request_forbids_placeholders()
    {
        var draft = $"Wir bitten um Rückmeldung {LongPlaceholder}.";

        var check = Single(
            DraftCheckEvaluator.Evaluate(draft, OrderConstraints.None with { ForbidPlaceholders = true, ForbidPlaceholdersQuote = "ohne Platzhalter" }),
            DraftCheckCodes.Placeholders);

        Assert.False(check.Passed);
        Assert.True(check.Blocking);
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

    private static OrderConstraints Forbid(ForbiddenPattern pattern) =>
        OrderConstraints.None with { ForbiddenPatterns = [new PatternRule(pattern, "rule")] };

    [Theory]
    [InlineData("24. November 2026\n\nSehr geehrter Herr Meier,\n\nvielen Dank für Ihr Schreiben.")]
    [InlineData("Berlin, 24. November 2026\nSehr geehrter Herr Meier, vielen Dank.")]
    [InlineData("3. May 2026\n\nDear Ms Smith,\n\nthank you for your letter.")]
    [InlineData("12. Mai\n1. März\nZwei Termine stehen fest.")]
    [InlineData("1. Advent: Wir laden Sie herzlich zu unserem Adventskaffee ein.")]
    [InlineData("Ein Absatz ohne Liste. Im Jahr 2024 - so heißt es - war alles gut.")]
    [InlineData("Erster Teil der Geschichte.\n\n* * *\n\nZweiter Teil der Geschichte.")]
    [InlineData("Erster Teil.\n- - -\nZweiter Teil.\n***\nDritter Teil.")]
    [InlineData("Erster Teil.\n•••\nZweiter Teil.")]
    public void Dates_and_single_numbered_lines_are_not_lists(string draft)
    {
        Assert.True(Single(DraftCheckEvaluator.Evaluate(draft, Forbid(ForbiddenPattern.Lists)), DraftCheckCodes.Lists).Passed);
    }

    [Theory]
    [InlineData("Drei Gründe:\n1. Erstens das.\n2. Zweitens dies.")]
    [InlineData("Drei Gründe:\n\n1) Erstens das.\n\n2) Zweitens dies.")]
    [InlineData("Drei Gründe:\n- Erstens das.")]
    [InlineData("Three reasons:\n• First this.\n• Then that.")]
    [InlineData("Teil eins.\n* * *\n* Ein Punkt.")]
    public void Real_lists_are_found(string draft)
    {
        Assert.False(Single(DraftCheckEvaluator.Evaluate(draft, Forbid(ForbiddenPattern.Lists)), DraftCheckCodes.Lists).Passed);
    }

    [Fact]
    public void A_ban_of_subheadings_allows_the_title()
    {
        const string titled = "# Mein Titel\n\nEin Absatz Fließtext über das Thema.";
        const string sectioned = "# Mein Titel\n\nEin Absatz.\n\n## Abschnitt\n\nNoch ein Absatz.";

        Assert.True(Single(DraftCheckEvaluator.Evaluate(titled, Forbid(ForbiddenPattern.Subheadings)), DraftCheckCodes.Headings).Passed);
        Assert.False(Single(DraftCheckEvaluator.Evaluate(sectioned, Forbid(ForbiddenPattern.Subheadings)), DraftCheckCodes.Headings).Passed);
        Assert.False(Single(DraftCheckEvaluator.Evaluate(titled, Forbid(ForbiddenPattern.Headings)), DraftCheckCodes.Headings).Passed);
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
