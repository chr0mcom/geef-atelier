using Geef.Atelier.Core.Domain.Checks;

namespace Geef.Atelier.Tests.Domain.Checks;

/// <summary>
/// Fixes when a length becomes a blocking check: only when the whole request is clear about it. Every example of the
/// third review stays a note; clear requests in German and English keep their blocking rule.
/// </summary>
public sealed class RequestConstraintGateTests
{
    private const string UncheckedNoteEnd = "it was not checked automatically.";

    [Theory]
    [InlineData("Write 3 tweets about coffee, max 280 characters.")]
    [InlineData("Write three tweets of at most 280 characters about coffee.")]
    [InlineData("Schreibe 4 Slogans für meine Bäckerei mit höchstens 12 Wörtern.")]
    [InlineData("Erstelle 10 Produkttitel mit maximal 60 Zeichen.")]
    [InlineData("Write a FAQ with 6 questions, answers up to 60 words.")]
    [InlineData("Write one description per product, at most 80 words.")]
    [InlineData("Write one description for every product, at most 80 words.")]
    [InlineData("Schreibe 3 E-Mails mit ca. 100 Wörtern.")]
    public void Golden_table_of_the_third_review_several_pieces(string request) => AssertNoRule(request);

    [Theory]
    [InlineData("Schreibe einen LinkedIn-Post über unsere 10.000-Wörter-Studie.")]
    [InlineData("Schreibe einen Blogpost über das 280-Zeichen-Limit.")]
    [InlineData("Write a blog post about the 280-character limit.")]
    [InlineData("Write a text about the 500-word rule.")]
    [InlineData("Schreibe eine Mail an den Autor des 5.000-Wörter-Berichts.")]
    [InlineData("Schreibe einen Titel für ein Buch mit mindestens 80.000 Wörtern.")]
    [InlineData("Schreibe einen Teaser für den Artikel (mehr als 2.000 Wörter).")]
    [InlineData("Wir haben einen Artikel mit mehr als 2.000 Wörtern. Schreibe die Einleitung dazu.")]
    public void Golden_table_of_the_third_review_numbers_of_other_things(string request) => AssertNoRule(request);

    [Theory]
    [InlineData("Der letzte Text hatte mehr als 800 Wörter, das war zu lang. Schreibe einen kürzeren.")]
    [InlineData("The previous version had more than 600 words, which was too long. Make it shorter.")]
    [InlineData("Mein Entwurf hat mehr als 900 Wörter. Kürze ihn bitte auf das Wesentliche.")]
    [InlineData("My draft has more than 900 words. Please cut it down.")]
    [InlineData("Letztes Mal waren es maximal 100 Wörter, das war zu kurz. Schreibe mehr.")]
    [InlineData("Der Text ist zu kurz, er hat nur höchstens 100 Wörter. Mach ihn länger.")]
    [InlineData("Schreibe einen Text, der den Text von vorhin (mehr als 500 Wörter) ersetzt.")]
    public void Golden_table_of_the_third_review_earlier_texts(string request) => AssertNoRule(request);

    [Theory]
    [InlineData("Reply to this email: \"Please send us at least 2000 words by Friday.\" Say that I can do it.")]
    [InlineData("Explain what \"at most 100 words\" means.")]
    [InlineData("Schreibe eine Anleitung, wie man \"mindestens 300 Wörter\" schreibt.")]
    [InlineData("Das Zitat lautet \"mehr als 1.000 Wörter\". Schreibe einen Kommentar dazu.")]
    [InlineData("Beantworte diese Anfrage: „Bitte mindestens 500 Wörter zum Thema Kaffee“ und sage höflich ab.")]
    public void Golden_table_of_the_third_review_quotes(string request) => AssertNoRule(request);

    [Theory]
    [InlineData("Schreibe einen Blogbeitrag über Kaffee, höchstens 300 Wörter.", LengthUnit.Words, null, 300)]
    [InlineData("Schreibe einen Text mit höchstens 100 Wörtern.", LengthUnit.Words, null, 100)]
    [InlineData("Verfasse eine Einladung zum Sommerfest, maximal 150 Wörter.", LengthUnit.Words, null, 150)]
    [InlineData("Schreibe mindestens 500 Wörter über das Thema.", LengthUnit.Words, 500, null)]
    [InlineData("Schreibe ca. 300 Wörter über Bienen.", LengthUnit.Words, 300, 300)]
    [InlineData("Schreibe eine Mail, zwischen 100 und 150 Wörter.", LengthUnit.Words, 100, 150)]
    [InlineData("Formuliere eine kurze E-Mail (max. 200 Wörter) an den Vermieter.", LengthUnit.Words, null, 200)]
    [InlineData("Die Antwort soll höchstens 150 Wörter haben.", LengthUnit.Words, null, 150)]
    [InlineData("Bitte nicht mehr als 300 Wörter.", LengthUnit.Words, null, 300)]
    [InlineData("Ein LinkedIn-Post, höchstens 1.300 Zeichen.", LengthUnit.Characters, null, 1300)]
    [InlineData("Schreibe mehr als 100 Wörter und nicht mehr als 200 Wörter.", LengthUnit.Words, 100, 200)]
    [InlineData("Elternbrief zum Wandertag: zwischen 150 und 220 Wörter, bitte sachlich.", LengthUnit.Words, 150, 220)]
    [InlineData("Schreibe einen Text von genau 250 Wörtern über den Herbst.", LengthUnit.Words, 250, 250)]
    [InlineData("Schreibe einen kurzen, sachlichen Absatz (80–120 Wörter) über Datensicherung.", LengthUnit.Words, 80, 120)]
    [InlineData("Write a post of at most 150 words.", LengthUnit.Words, null, 150)]
    [InlineData("Write a blog post of between 120 and 180 words about coffee.", LengthUnit.Words, 120, 180)]
    [InlineData("Keep it under 120 words.", LengthUnit.Words, null, 120)]
    [InlineData("Write at least 800 words on the topic.", LengthUnit.Words, 800, null)]
    [InlineData("Write about 300 words on bees.", LengthUnit.Words, 300, 300)]
    [InlineData("Write a tweet of at most 280 characters.", LengthUnit.Characters, null, 280)]
    [InlineData("Write a thank-you note to the parents' committee after the school fair. Keep it under 90 words.", LengthUnit.Words, null, 90)]
    [InlineData("Write a cover letter — not more than 200 words and no exclamation marks.", LengthUnit.Words, null, 200)]
    [InlineData("The text must not be more than 150 words.", LengthUnit.Words, null, 150)]
    [InlineData("Draft a short welcome speech, around 400 words.", LengthUnit.Words, 400, 400)]
    [InlineData("Write a press release about the opening of our tennis courts. Between 300 and 400 words.", LengthUnit.Words, 300, 400)]
    [InlineData("Exactly 90 words: a job ad for a pharmacy technician.", LengthUnit.Words, 90, 90)]
    [InlineData("Write it in no more than 200 words.", LengthUnit.Words, null, 200)]
    public void Clear_requests_keep_a_blocking_rule(string request, LengthUnit unit, int? min, int? max)
    {
        var length = RequestConstraintExtractor.Extract(request).Length;

        Assert.NotNull(length);
        Assert.Equal(unit, length.Unit);
        Assert.Equal(min, length.Min);
        Assert.Equal(max, length.Max);
    }

    [Theory]
    [InlineData("Schreibe zwei Varianten, je höchstens 50 Wörter.")]
    [InlineData("Write a few options for the headline, at most 12 words.")]
    [InlineData("Write tweets about our launch, max 280 characters.")]
    [InlineData("Write a speech of at most 500 words, about 3 minutes long.")]
    [InlineData("Schreibe eine Rede für eine Minute, höchstens 150 Wörter.")]
    public void Other_counts_in_the_request_leave_the_length_a_note(string request) => AssertNoRule(request);

    [Theory]
    [InlineData("Überarbeite den Text, höchstens 200 Wörter.")]
    [InlineData("Rewrite this, at most 150 words.")]
    [InlineData("Translate it into English, at most 200 words.")]
    [InlineData("Verfasse eine Zusammenfassung in maximal 200 Wörtern.")]
    [InlineData("Write a summary of the report, at most 200 words.")]
    [InlineData("Das war zu lang. Höchstens 150 Wörter.")]
    public void Revisions_and_summaries_leave_the_length_a_note(string request) => AssertNoRule(request);

    [Theory]
    [InlineData("Schreibe einen Text mit 300 Wörtern.", 300)]
    [InlineData("Write a post of 150 words.", 150)]
    public void A_length_right_after_the_text_to_write_needs_no_limit_word(string request, int number)
    {
        var length = RequestConstraintExtractor.Extract(request).Length;

        Assert.NotNull(length);
        Assert.Equal(number, length.Min);
        Assert.Equal(number, length.Max);
    }

    [Theory]
    [InlineData("Schreibe einen Post über Bienen, 300 Wörter.")]
    [InlineData("Länge: 300 Wörter.")]
    [InlineData("So 200 Wörter reichen: eine Einladung.")]
    [InlineData("Write a blog post about coffee in 300 words.")]
    public void A_length_without_a_limit_word_stays_a_note(string request) => AssertNoRule(request);

    [Theory]
    [InlineData("Write a blurb for my novel, which has more than 80,000 words.")]
    [InlineData("Schreibe einen Klappentext, das Buch hat mehr als 80.000 Wörter.")]
    [InlineData("Unser Bericht ist mindestens 2.000 Wörter lang. Schreibe eine Einleitung.")]
    [InlineData("Write an intro and the article has more than 2,000 words.")]
    public void A_length_of_something_the_request_talks_about_stays_a_note(string request) => AssertNoRule(request);

    [Fact]
    public void A_length_inside_quotes_is_named_in_a_note()
    {
        var constraints = RequestConstraintExtractor.Extract("Explain what \"at most 100 words\" means.");

        Assert.Null(constraints.Length);
        Assert.Contains("Your request mentions a length (\"at most 100 words\"); it was not checked automatically.", constraints.Notes);
    }

    [Fact]
    public void A_length_outside_quotes_is_read_while_the_quoted_one_is_ignored()
    {
        var length = RequestConstraintExtractor.Extract("Unser Style Guide sagt: „Texte sollten mindestens 2.000 Wörter haben.“ Schreib eine Erinnerungs-Mail ans Team, maximal 60 Wörter.").Length;

        Assert.NotNull(length);
        Assert.Null(length.Min);
        Assert.Equal(60, length.Max);
    }

    [Fact]
    public void Text_attached_after_a_lead_in_is_not_read()
    {
        AssertNoRule("Antworte auf diese Mail:\nHallo, bitte schicken Sie uns mindestens 500 Wörter zum Angebot.\nViele Grüße");
    }

    private static void AssertNoRule(string request)
    {
        var constraints = RequestConstraintExtractor.Extract(request);

        Assert.Null(constraints.Length);
        Assert.NotEmpty(constraints.Notes);
        Assert.DoesNotContain(DraftCheckEvaluator.Evaluate(string.Join(' ', Enumerable.Repeat("Wort", 5000)), constraints), r => r.Code == DraftCheckCodes.Length);
    }

    [Fact]
    public void Every_request_with_a_length_that_is_not_checked_has_the_plain_note()
    {
        var constraints = RequestConstraintExtractor.Extract("Write 3 tweets about coffee, max 280 characters.");

        Assert.Contains(constraints.Notes, n => n.StartsWith("Your request mentions a length (", StringComparison.Ordinal) && n.EndsWith(UncheckedNoteEnd, StringComparison.Ordinal));
    }
}
