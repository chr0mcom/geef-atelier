using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Tests.Domain.Checks;

public sealed class RequestConstraintExtractorTests
{
    [Theory]
    [InlineData("Schreibe einen Blogbeitrag mit 120–180 Wörtern über Kaffee.", LengthKind.Range, 120, 180)]
    [InlineData("Write a blog post of between 120 and 180 words about coffee.", LengthKind.Range, 120, 180)]
    [InlineData("Schreibe eine Mail, zwischen 100 und 150 Wörter.", LengthKind.Range, 100, 150)]
    [InlineData("Formuliere eine kurze E-Mail (max. 200 Wörter) an den Vermieter.", LengthKind.AtMost, null, 200)]
    [InlineData("Die Antwort soll höchstens 150 Wörter haben.", LengthKind.AtMost, null, 150)]
    [InlineData("Bitte nicht mehr als 300 Wörter.", LengthKind.AtMost, null, 300)]
    [InlineData("Schreibe einen Text, bis zu 250 Wörter.", LengthKind.AtMost, null, 250)]
    [InlineData("Write it in no more than 200 words.", LengthKind.AtMost, null, 200)]
    [InlineData("Keep it under 120 words.", LengthKind.AtMost, null, 120)]
    [InlineData("Up to 90 words, please.", LengthKind.AtMost, null, 90)]
    [InlineData("Schreibe mindestens 500 Wörter über das Thema.", LengthKind.AtLeast, 500, null)]
    [InlineData("Write at least 800 words on the topic.", LengthKind.AtLeast, 800, null)]
    [InlineData("Schreibe ca. 300 Wörter über Bienen.", LengthKind.About, 300, 300)]
    [InlineData("Schreibe etwa 300 Wörter über Bienen.", LengthKind.About, 300, 300)]
    [InlineData("Write about 300 words on bees.", LengthKind.About, 300, 300)]
    [InlineData("Ein LinkedIn-Post, höchstens 1.300 Zeichen.", LengthKind.AtMost, null, 1300)]
    [InlineData("Write a tweet of at most 280 characters.", LengthKind.AtMost, null, 280)]
    [InlineData("Erstelle eine Rede (Richtwert 1000 bis 1150 Wörter).", LengthKind.About, 1000, 1150)]
    [InlineData("Schreibe einen kurzen, sachlichen Absatz (80–120 Wörter) über Datensicherung.", LengthKind.Range, 80, 120)]
    public void Reads_lengths(string request, LengthKind kind, int? min, int? max)
    {
        var length = RequestConstraintExtractor.Extract(request).Length;

        Assert.NotNull(length);
        Assert.Equal(kind, length.Kind);
        Assert.Equal(min, length.Min);
        Assert.Equal(max, length.Max);
        Assert.False(string.IsNullOrWhiteSpace(length.Quote));
    }

    [Theory]
    [InlineData("Fasse den Bericht in 200 Wörtern zusammen.")]
    [InlineData("Fasse das angehängte PDF in höchstens 200 Wörtern zusammen.")]
    [InlineData("Schreibe einen 300-Wörter-Text über Bienen.")]
    [InlineData("Write a 300-word post about bees.")]
    [InlineData("Richtwert: ca. 150–220 Wörter, maximal ca. 250 Wörter.")]
    public void Lengths_that_the_whole_request_does_not_make_clear_stay_notes(string request)
    {
        var constraints = RequestConstraintExtractor.Extract(request);

        Assert.Null(constraints.Length);
        Assert.Contains(constraints.Notes, n => n.EndsWith("it was not checked automatically.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("The text must not be more than 150 words.", 150)]
    [InlineData("Don't write more than 100 words.", 100)]
    [InlineData("Don’t write more than 120 words.", 120)]
    [InlineData("Please do not use more than 200 words for the reply.", 200)]
    [InlineData("Write a short note to the team, never more than 100 words.", 100)]
    [InlineData("Auf keinen Fall mehr als 100 Wörter.", 100)]
    [InlineData("Nie mehr als 100 Wörter bitte.", 100)]
    [InlineData("Mehr als 100 Wörter sollen es nicht sein.", 100)]
    [InlineData("Der Text darf nicht mehr als 250 Wörter haben.", 250)]
    [InlineData("Schreibe ohne mehr als 80 Wörter zu brauchen eine Einladung.", 80)]
    [InlineData("It must not exceed 300 words.", 300)]
    public void A_negated_more_than_is_an_upper_limit(string request, int max)
    {
        var length = RequestConstraintExtractor.Extract(request).Length;

        Assert.NotNull(length);
        Assert.Equal(LengthKind.AtMost, length.Kind);
        Assert.Null(length.Min);
        Assert.Equal(max, length.Max);
    }

    [Theory]
    [InlineData("Write more than 100 words.", 100)]
    [InlineData("Mehr als 100 Wörter bitte.", 100)]
    [InlineData("Write more than 300 words, not less.", 300)]
    [InlineData("Not less than 300 words, please.", 300)]
    [InlineData("Nicht weniger als 300 Wörter.", 300)]
    [InlineData("Der Text soll nicht unter 400 Wörter haben.", 400)]
    [InlineData("Keep it not under 200 words.", 200)]
    public void More_than_without_negation_and_negated_less_than_are_lower_limits(string request, int min)
    {
        var length = RequestConstraintExtractor.Extract(request).Length;

        Assert.NotNull(length);
        Assert.Equal(LengthKind.AtLeast, length.Kind);
        Assert.Equal(min, length.Min);
        Assert.Null(length.Max);
    }

    [Theory]
    [InlineData("Ich will keinen Text mit mehr als 300 Wörtern.")]
    [InlineData("More than 300 words is not what I want.")]
    [InlineData("Der Text soll keine mehr als 300 Wörter haben.")]
    public void A_negation_elsewhere_in_the_clause_leaves_the_length_unchecked(string request)
    {
        var constraints = RequestConstraintExtractor.Extract(request);

        Assert.Null(constraints.Length);
        Assert.Contains(constraints.Notes, n => n.StartsWith("A length next to a negation", StringComparison.Ordinal));
    }

    [Fact]
    public void A_turned_limit_quotes_the_negation()
    {
        var length = RequestConstraintExtractor.Extract("The text must not be more than 150 words.").Length;

        Assert.Equal("not be more than 150 words", length!.Quote);
    }

    [Theory]
    [InlineData("Übernimm die 200 Wörter Einleitung des Originals und schreibe den Rest neu.")]
    [InlineData("Fasse den 5000 Wörter langen Text zusammen.")]
    [InlineData("Der Originaltext hat etwa 2000 Wörter. Kürze ihn deutlich.")]
    [InlineData("The article has about 3000 words; summarise it.")]
    [InlineData("Die Betreffzeile soll höchstens 60 Zeichen haben.")]
    [InlineData("Schreibe einen Bericht, die Einleitung (max. 100 Wörter) soll das Problem nennen.")]
    [InlineData("Write a report; each section at most 200 words.")]
    [InlineData("Schreibe drei Absätze mit je 50 Wörtern.")]
    [InlineData("maximal 2.200 Wörter Fließtext (ohne Quellenverzeichnis). Zielkorridor 1.900–2.200 Wörter.")]
    [InlineData("Schreibe max. 200 Wörter, aber mindestens 300 Wörter.")]
    [InlineData("Schreibe max. 200 Wörter oder höchstens 900 Zeichen.")]
    [InlineData("Das Kapitel umfasst ca. 40.000 Zeichen.")]
    [InlineData("Schreibe eine Glückwunschkarte zum 50. Geburtstag.")]
    [InlineData("Schreibe max. 3 Wörter als Titel.")]
    public void Leaves_unclear_lengths_unchecked(string request)
    {
        var constraints = RequestConstraintExtractor.Extract(request);

        Assert.Null(constraints.Length);
    }

    [Fact]
    public void Records_a_note_for_a_length_it_does_not_check()
    {
        var constraints = RequestConstraintExtractor.Extract("Die Betreffzeile soll höchstens 60 Zeichen haben.");

        Assert.Contains(constraints.Notes, n => n.Contains("part of the text", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Schreibe zwei Seiten über das Thema, höchstens 3 Absätze.")]
    [InlineData("Write it in 5 sentences.")]
    public void Pages_paragraphs_and_sentences_are_notes_only(string request)
    {
        var constraints = RequestConstraintExtractor.Extract(request);

        Assert.Null(constraints.Length);
        Assert.Contains(constraints.Notes, n => n.StartsWith("Pages, paragraphs", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Schreibe eine kurze Einladung zum Sommerfest auf Englisch.", "en")]
    [InlineData("Write a short invitation to the summer party in German.", "de")]
    [InlineData("Übersetze den folgenden Absatz ins Englische.", "en")]
    [InlineData("Translate the paragraph into German.", "de")]
    [InlineData("Schreibe einen LinkedIn-Beitrag (deutschsprachig) über unser neues Produkt.", "de")]
    [InlineData("Schreibe eine kurze Einladung zum Sommerfest für die Nachbarschaft und bitte um eine Zusage bis Freitag, damit wir planen können.", "de")]
    [InlineData("Please write a short invitation to the summer party for our neighbours and ask them to reply by Friday so that we can plan.", "en")]
    [InlineData("Die Quelle ist auf Englisch. Schreibe die Zusammenfassung auf Deutsch.", "de")]
    public void Reads_the_target_language(string request, string language)
    {
        Assert.Equal(language, RequestConstraintExtractor.Extract(request).Language);
    }

    [Theory]
    [InlineData("Schreibe eine englische E-Mail an den Kunden und bitte um eine Antwort bis Freitag, damit wir planen können.")]
    [InlineData("Übersetze den Text.")]
    [InlineData("Schreibe den Text auf Französisch.")]
    [InlineData("Schreibe den Text auf Deutsch und auf Englisch.")]
    [InlineData("Kurz.")]
    [InlineData("Antworte auf diese E-Mail: Dear Mr Smith, thank you for your offer. We would like to know whether the price includes delivery and what the payment terms are. Best regards")]
    public void Leaves_unclear_languages_unchecked(string request)
    {
        Assert.Null(RequestConstraintExtractor.Extract(request).Language);
    }

    [Theory]
    [InlineData("Keine Platzhalter.")]
    [InlineData("Der Brief muss versandfertig sein, ohne Platzhalter.")]
    [InlineData("Lass keine offenen Platzhalter stehen.")]
    [InlineData("No placeholders, please.")]
    [InlineData("Do not use placeholders.")]
    public void Reads_a_placeholder_ban(string request)
    {
        var constraints = RequestConstraintExtractor.Extract(request);

        Assert.True(constraints.ForbidPlaceholders);
        Assert.NotNull(constraints.ForbidPlaceholdersQuote);
    }

    [Theory]
    [InlineData("Ersetze alle Platzhalter durch echte Angaben.")]
    [InlineData("Platzhalter wie [Datum] sind in Ordnung.")]
    public void Does_not_read_a_placeholder_ban_where_there_is_none(string request)
    {
        Assert.False(RequestConstraintExtractor.Extract(request).ForbidPlaceholders);
    }

    [Theory]
    [InlineData("Keine Aufzählungen, kein Marketington.", ForbiddenPattern.Lists)]
    [InlineData("Bitte ohne Stichpunkte.", ForbiddenPattern.Lists)]
    [InlineData("Write it without bullet points.", ForbiddenPattern.Lists)]
    [InlineData("No lists.", ForbiddenPattern.Lists)]
    [InlineData("Ohne Zwischenüberschriften.", ForbiddenPattern.Subheadings)]
    [InlineData("Ohne Überschriften.", ForbiddenPattern.Headings)]
    [InlineData("Write it without subheadings.", ForbiddenPattern.Subheadings)]
    [InlineData("No headings, please.", ForbiddenPattern.Headings)]
    [InlineData("Keine Emojis.", ForbiddenPattern.Emojis)]
    [InlineData("Do not use emojis.", ForbiddenPattern.Emojis)]
    public void Reads_forbidden_forms(string request, ForbiddenPattern pattern)
    {
        var rule = Assert.Single(RequestConstraintExtractor.Extract(request).ForbiddenPatterns);

        Assert.Equal(pattern, rule.Pattern);
    }

    [Theory]
    [InlineData("Keine Aufzählung aller Details.")]
    [InlineData("Keine Liste der Teilnehmer, aber eine kurze Begrüßung.")]
    [InlineData("Nicht ohne Aufzählung bitte.")]
    public void Does_not_read_forbidden_forms_from_content_wishes(string request)
    {
        Assert.Empty(RequestConstraintExtractor.Extract(request).ForbiddenPatterns);
    }

    [Theory]
    [InlineData("Verwende nicht das Wort „Synergie“.", "Synergie")]
    [InlineData("Keine Formulierung wie \"nach einvernehmlicher Trennung\" oder Ähnliches.", "nach einvernehmlicher Trennung")]
    [InlineData("Vermeide „innovativ“.", "innovativ")]
    [InlineData("Bitte ohne „Sehr geehrte Damen und Herren“.", "Sehr geehrte Damen und Herren")]
    [InlineData("„Gamechanger“ darf nicht vorkommen.", "Gamechanger")]
    [InlineData("Don't use \"leverage\".", "leverage")]
    [InlineData("Never write \"synergy\".", "synergy")]
    [InlineData("Avoid the word \"disrupt\".", "disrupt")]
    public void Reads_forbidden_terms(string request, string term)
    {
        var rule = Assert.Single(RequestConstraintExtractor.Extract(request).ForbiddenTerms);

        Assert.Equal(term, rule.Term);
    }

    [Fact]
    public void Reads_several_forbidden_terms_in_one_list()
    {
        var terms = RequestConstraintExtractor.Extract("Keine Floskeln wie „zeitnah“, „proaktiv“ oder „Mehrwert“.").ForbiddenTerms;

        Assert.Equal(["zeitnah", "proaktiv", "Mehrwert"], terms.Select(t => t.Term));
    }

    [Theory]
    [InlineData("Der Text muss den Begriff „Kundennummer 4711“ enthalten.", "Kundennummer 4711")]
    [InlineData("Erwähne unbedingt „Tag der offenen Tür“.", "Tag der offenen Tür")]
    [InlineData("„Frist: 30. Juni“ muss vorkommen.", "Frist: 30. Juni")]
    [InlineData("The email must mention \"order 1234\".", "order 1234")]
    [InlineData("Make sure to include \"free parking\".", "free parking")]
    public void Reads_required_terms(string request, string term)
    {
        var rule = Assert.Single(RequestConstraintExtractor.Extract(request).RequiredTerms);

        Assert.Equal(term, rule.Term);
    }

    [Theory]
    [InlineData("Der Ton soll „freundlich“ klingen.")]
    [InlineData("Er sagte „nicht heute“ und ging.")]
    public void Does_not_read_terms_from_quotes_without_an_instruction(string request)
    {
        var constraints = RequestConstraintExtractor.Extract(request);

        Assert.Empty(constraints.ForbiddenTerms);
        Assert.Empty(constraints.RequiredTerms);
    }

    [Fact]
    public void A_term_both_required_and_forbidden_is_dropped_with_a_note()
    {
        var constraints = RequestConstraintExtractor.Extract("Vermeide „Rabatt“. Der Text muss „Rabatt“ enthalten.");

        Assert.Empty(constraints.ForbiddenTerms);
        Assert.Empty(constraints.RequiredTerms);
        Assert.NotEmpty(constraints.Notes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_requests_give_no_constraints(string request)
    {
        Assert.Same(OrderConstraints.None, RequestConstraintExtractor.Extract(request));
    }
}
