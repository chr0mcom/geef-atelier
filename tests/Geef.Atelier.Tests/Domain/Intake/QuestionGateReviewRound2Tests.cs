using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// Review round 2 of stage 2: only a question about the length of the text itself becomes the length template; an asked topic gets
/// no assumption beside the answer; bare names from the request are fine, links never; every secret word is dropped unless the
/// request of the person treats the topic, and a request for the secret itself is always dropped.
/// </summary>
public sealed class QuestionGateReviewRound2Tests
{
    private const string BankRequest = "Schreib eine kurze Nachricht an die Bank.";
    private const string PasswordGuideRequest = "Schreib einen Ratgeber zu Passwortsicherheit für unser Team.";

    private static readonly IReadOnlyDictionary<GapTopic, MemoryAnswer> Nothing = new Dictionary<GapTopic, MemoryAnswer>();

    private static QuestionGateResult Apply(string request, string language, params IntakeGap[] gaps) =>
        Apply(request, language, 3, gaps);

    private static QuestionGateResult Apply(string request, string language, int maxQuestions, params IntakeGap[] gaps) =>
        QuestionGate.Apply(new QuestionGateInput(QuestionGateTests.Output(gaps, language), request, false, Nothing, maxQuestions));

    private static QuestionGateResult ApplyUnderstanding(string request, string understanding) =>
        QuestionGate.Apply(new QuestionGateInput(QuestionGateTests.Output([]) with { Understanding = understanding }, request, false, Nothing, 3));

    public static TheoryData<string, string, GapTopic, string, string[]> FactQuestionsAboutADuration() => new()
    {
        { "Schreib einen Urlaubsantrag an meine Chefin.", "de", GapTopic.Facts, "Wie lange soll dein Urlaub dauern?", ["Eine Woche", "Zwei Wochen", "Drei Wochen"] },
        { "Write a cover letter for a nursing job.", "en", GapTopic.Facts, "How long have you worked as a nurse?", ["Under two years", "Two to five years", "Over five years"] },
        { "Write a short note to the landlord about the new lease.", "en", GapTopic.Other, "Do you want a long-term or a rolling lease?", ["Long-term", "Rolling"] },
        { "Schreib eine Schadensmeldung an die Versicherung.", "de", GapTopic.Facts, "Welchen Umfang hat der Schaden?", ["Klein", "Mittel", "Groß"] },
        { "Schreib eine Schadensmeldung an die Versicherung.", "de", GapTopic.Scope, "Welchen Umfang hat der Schaden?", ["Klein", "Mittel", "Groß"] },
        { "Schreib eine Nachricht an den Vermieter.", "de", GapTopic.Other, "Soll die Frist länger sein?", ["Ja", "Nein"] },
        { "Write a letter to my employer.", "en", GapTopic.Other, "How long is the notice period?", ["One month", "Three months"] },
        { "Schreib eine Bewerbung als Pflegekraft.", "de", GapTopic.Scope, "In welchem Umfang möchtest du arbeiten?", ["Vollzeit", "Teilzeit"] },
        { "Schreib eine Einladung zum Workshop.", "de", GapTopic.Format, "Wie lange dauert der Workshop?", ["Ein halber Tag", "Ein ganzer Tag"] },
    };

    [Theory]
    [MemberData(nameof(FactQuestionsAboutADuration))]
    public void A_question_about_a_duration_or_an_amount_is_asked_as_it_is_and_never_becomes_the_length_template(
        string request, string language, GapTopic topic, string text, string[] options)
    {
        Assert.False(LengthMentions.AsksForTextLength(new ProposedQuestion(text, options, null, false)));

        var result = Apply(request, language, QuestionGateTests.Ask(topic, text, options, assumption: options[0]));

        var question = Assert.Single(result.Questions);
        Assert.False(question.FromTemplate);
        Assert.Equal(text, question.Text);
        Assert.Equal(topic, question.Slot);
    }

    [Fact]
    public void A_fact_question_about_a_duration_stays_when_the_request_names_a_length()
    {
        var result = Apply("Schreib in etwa 100 Wörtern eine Kündigung für meine Wohnung.", "de",
            QuestionGateTests.Ask(GapTopic.Facts, "Wie lange wohnst du schon dort?", ["Unter 5 Jahre", "Über 5 Jahre"], assumption: "Unter 5 Jahre"));

        Assert.Equal("Wie lange wohnst du schon dort?", Assert.Single(result.Questions).Text);
        Assert.Equal(GateDecision.Asked, Assert.Single(result.Trace).Decision);
        Assert.DoesNotContain(result.Assumptions, a => a.Text == "Unter 5 Jahre");
    }

    [Fact]
    public void A_real_length_gap_and_a_question_about_the_duration_of_an_event_are_both_asked()
    {
        var result = Apply("Schreib eine Einladung zum Workshop.", "de",
            QuestionGateTests.Ask(GapTopic.Length, "", []),
            QuestionGateTests.Ask(GapTopic.Facts, "Wie lange dauert der Workshop?", ["Ein halber Tag", "Ein ganzer Tag"], assumption: "Ein halber Tag"));

        Assert.Equal([QuestionTemplates.LengthQuestionId, "q1"], result.Questions.Select(q => q.Id));
        Assert.Equal("Wie lange dauert der Workshop?", result.Questions[1].Text);
        Assert.DoesNotContain(result.Assumptions, a => a.Text == "Ein halber Tag");
    }

    [Theory]
    [InlineData("Wie lang soll der Beitrag werden?")]
    [InlineData("Welchen Umfang soll der Text haben?")]
    [InlineData("Wie lang und ausführlich soll es sein?")]
    [InlineData("Soll der Einladungstext eher kurz oder lang sein?")]
    [InlineData("How detailed and long should it be?")]
    [InlineData("About how long should the post be?")]
    [InlineData("How long would you like the letter to be?")]
    [InlineData("Wie viele Wörter?")]
    public void A_question_about_the_length_of_the_text_itself_is_recognised(string text) =>
        Assert.True(LengthMentions.AsksForTextLength(new ProposedQuestion(text, ["Kurz", "Lang"], null, false)), text);

    [Fact]
    public void A_question_about_the_length_of_the_text_under_facts_or_audience_keeps_its_topic()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de",
            QuestionGateTests.Ask(GapTopic.Facts, "Wie lang soll der Text werden?", ["Kurz", "Lang"]));

        Assert.False(Assert.Single(result.Questions).FromTemplate);
    }

    [Fact]
    public void A_dropped_duplicate_of_the_asked_length_gives_no_assumption_beside_the_answer()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de",
            QuestionGateTests.Ask(GapTopic.Length, "", []),
            QuestionGateTests.Ask(GapTopic.Scope, "Wie lang und ausführlich soll es sein?", ["Kurz", "Ausführlich"], assumption: "Ein ausführlicher Text von etwa zwei Seiten"));

        Assert.Equal(QuestionTemplates.LengthQuestionId, Assert.Single(result.Questions).Id);
        Assert.Equal(GateDecision.DroppedDuplicateTopic, result.Trace[1].Decision);
        Assert.DoesNotContain(result.Assumptions, a => a.Topic == GapTopic.Length);
        Assert.DoesNotContain(result.Assumptions, a => a.Text.Contains("zwei Seiten", StringComparison.Ordinal));
    }

    [Fact]
    public void Without_a_model_assumption_no_not_specified_line_is_set_beside_an_asked_topic()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de",
            QuestionGateTests.Ask(GapTopic.Length, "", []),
            QuestionGateTests.Ask(GapTopic.Format, "Wie lang soll der Beitrag werden?", ["Kurz", "Lang"], assumption: null));

        Assert.DoesNotContain(result.Assumptions, a => a.Text == QuestionGate.NotSpecified(GapTopic.Length));
    }

    [Fact]
    public void A_second_audience_question_is_dropped_without_an_assumption_that_contradicts_the_answer()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de",
            QuestionGateTests.Ask(GapTopic.Audience, "Wen lädst du ein?", ["Nur das Team", "Auch die Familien"]),
            QuestionGateTests.Ask(GapTopic.Audience, "Kommen auch Familien?", ["Ja", "Nein"], assumption: "Eingeladen ist nur das Team, ohne Familien"));

        Assert.Equal("Wen lädst du ein?", Assert.Single(result.Questions).Text);
        Assert.Empty(result.Assumptions);
    }

    [Fact]
    public void A_duplicate_of_a_question_that_was_not_asked_keeps_its_assumption()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de", 1,
            QuestionGateTests.Ask(GapTopic.Length, "", []),
            QuestionGateTests.Ask(GapTopic.Audience, "Wen lädst du ein?", ["Nur das Team", "Auch die Familien"], assumption: "Nur das Team"),
            QuestionGateTests.Ask(GapTopic.Audience, "Kommen auch Familien?", ["Ja", "Nein"], assumption: "Ohne Familien"));

        Assert.Equal(GateDecision.DroppedOverLimit, result.Trace[1].Decision);
        Assert.Equal(GateDecision.DroppedDuplicateTopic, result.Trace[2].Decision);
        Assert.Contains(result.Assumptions, a => a.Text == "Ohne Familien");
    }

    [Fact]
    public void An_assumption_on_a_narrow_topic_that_is_asked_is_left_out_but_broad_topics_keep_theirs()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de",
            QuestionGateTests.Ask(GapTopic.Tone, "Wie soll der Ton sein?", ["Locker", "Förmlich"]),
            QuestionGateTests.Ask(GapTopic.Facts, "Wann beginnt das Fest?", ["Am Mittag", "Am Abend"]),
            QuestionGateTests.Gap(GapTopic.Tone, GapResolution.Assume, "Ein lockerer, herzlicher Ton"),
            QuestionGateTests.Gap(GapTopic.Facts, GapResolution.Assume, "Es gibt Kuchen und Kaffee"));

        Assert.Equal(2, result.Questions.Count);
        Assert.DoesNotContain(result.Assumptions, a => a.Topic == GapTopic.Tone);
        Assert.Contains(result.Assumptions, a => a.Text == "Es gibt Kuchen und Kaffee");
    }

    [Fact]
    public void A_unit_that_names_something_else_does_not_suppress_the_length_question()
    {
        var result = Apply("Im Buch fehlen zehn Seiten. Schreib eine Reklamation an den Verlag.", "de", QuestionGateTests.Ask(GapTopic.Length, "", []));

        Assert.Equal(QuestionTemplates.LengthQuestionId, Assert.Single(result.Questions).Id);
    }

    [Theory]
    [InlineData("Eine Beschwerde an Booking.com wegen einer Doppelbuchung.", "Schreib eine Beschwerde an Booking.com wegen einer Doppelbuchung.")]
    [InlineData("Die Rückfrage geht an Amazon.de.", "Schreib eine Rückfrage an Amazon.de wegen meiner Bestellung.")]
    [InlineData("Ein Anschreiben als Entwickler für Node.js.", "Schreib ein Anschreiben als Entwickler für Node.js.")]
    [InlineData("A cover letter for a developer role using ASP.NET.", "Write a cover letter for a developer role using asp.net.")]
    [InlineData("Eine Bewerbung für eine Stelle in St.Gallen.", "Schreib eine Bewerbung für eine Stelle in St.Gallen.")]
    public void A_bare_name_that_stands_in_the_request_is_no_link(string text, string request)
    {
        Assert.False(IntakeContentGuard.HasLinkOrAddress(text, request));
        Assert.True(IntakeContentGuard.HasLinkOrAddress(text, "Schreib eine Beschwerde wegen einer Doppelbuchung."));
    }

    [Fact]
    public void A_question_and_the_summary_with_a_name_from_the_request_are_kept()
    {
        const string request = "Schreib ein Anschreiben als Entwickler für Node.js in St.Gallen.";
        var output = QuestionGateTests.Output([QuestionGateTests.Ask(GapTopic.Facts, "Soll der Text Node.js erwähnen?", ["Ja", "Nein"])])
            with { Understanding = "Ein Anschreiben für eine Stelle mit Node.js in St.Gallen." };

        var result = QuestionGate.Apply(new QuestionGateInput(output, request, false, Nothing, 3));

        Assert.Single(result.Questions);
        Assert.Equal(output.Understanding, result.Understanding);
    }

    [Fact]
    public void A_name_that_is_only_in_a_file_or_only_like_one_in_the_request_is_dropped()
    {
        var result = Apply("Schreib eine Antwort auf den angehängten Brief.", "de",
            QuestionGateTests.Ask(GapTopic.Facts, "Soll ich auf booking.com verweisen?", ["Ja", "Nein"]));

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedUnsafeContent, Assert.Single(result.Trace).Decision);
        Assert.True(IntakeContentGuard.HasLinkOrAddress("Siehe booking.com", "Schreib an evilbooking.com."));
    }

    [Theory]
    [InlineData("Auf https://booking.com prüfen?", "Schreib an https://booking.com eine Beschwerde.")]
    [InlineData("Auf www.booking.com prüfen?", "Schreib an www.booking.com eine Beschwerde.")]
    [InlineData("Auf booking.com/login prüfen?", "Schreib an booking.com/login eine Beschwerde.")]
    [InlineData("Antwort an info@booking.com?", "Schreib an info@booking.com eine Beschwerde.")]
    [InlineData("Siehe [Booking](booking.com)", "Schreib an [Booking](booking.com) eine Beschwerde.")]
    public void Schemes_www_paths_mail_and_markdown_links_are_dropped_even_from_the_request(string text, string request) =>
        Assert.True(IntakeContentGuard.HasLinkOrAddress(text, request));

    [Theory]
    [InlineData("Auf müller-bank.de/login prüfen?")]
    [InlineData("Auf 192.168.1.5/login prüfen?")]
    [InlineData("Auf 192.168.1.5:8080 prüfen?")]
    [InlineData("Auf login.evil.md prüfen?")]
    [InlineData("Auf evil.md/login prüfen?")]
    [InlineData("Auf evil[.]example/login prüfen?")]
    [InlineData("Auf evil(dot)example prüfen?")]
    [InlineData("Auf evil。com prüfen?")]
    [InlineData("Auf evil​.​com prüfen?")]
    [InlineData("Auf еvil.com prüfen?")]
    [InlineData("Auf evil．com prüfen?")]
    [InlineData("Siehe [Anmelden][1]")]
    [InlineData("[1]: /login")]
    [InlineData("Auf //evil.example prüfen?")]
    public void Usual_ways_to_hide_a_link_are_found(string text) =>
        Assert.True(IntakeContentGuard.HasLinkOrAddress(text, BankRequest), text);

    [Theory]
    [InlineData("Soll ich notes.md als Grundlage nehmen?")]
    [InlineData("Soll Version 2.0 erwähnt werden?")]
    [InlineData("Ist 192.168.1.5 die Nummer aus dem Handbuch?")]
    [InlineData("Ein Brief an die Firma z.B. mit Datum.")]
    public void File_names_versions_and_abbreviations_are_no_links(string text) =>
        Assert.False(IntakeContentGuard.HasLinkOrAddress(text, BankRequest), text);

    public static TheoryData<string, string[]> SecretQuestions() => new()
    {
        { "Wie lautet das Passwort für das Firmenkonto?", ["Siehe Notiz", "Weiß ich nicht"] },
        { "Welches Passwort soll ich verwenden?", ["Das alte", "Ein neues"] },
        { "Welches Kennwort soll in den Brief?", ["Das alte", "Ein neues"] },
        { "Wie ist dein Firmenkennwort?", ["Weiß ich", "Weiß ich nicht"] },
        { "Wie lautet die PIN zur Karte?", ["Weiß ich", "Weiß ich nicht"] },
        { "Welche Geheimzahl hat die Karte?", ["Weiß ich", "Weiß ich nicht"] },
        { "Was soll noch rein?", ["Passwort ins Feld schreiben", "Nichts weiter"] },
        { "Soll die Karten-PIN in den Brief?", ["Ja", "Nein"] },
        { "Soll die pushTAN mit in den Text?", ["Ja", "Nein"] },
    };

    [Theory]
    [MemberData(nameof(SecretQuestions))]
    public void Every_question_or_option_with_a_secret_word_is_dropped_when_the_request_does_not_treat_the_topic(string text, string[] options)
    {
        var result = Apply(BankRequest, "de", QuestionGateTests.Ask(GapTopic.Facts, text, options, assumption: null));

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedUnsafeContent, Assert.Single(result.Trace).Decision);
    }

    [Theory]
    [InlineData("Wie lautet das Passwort für das Firmenkonto?")]
    [InlineData("Welches Passwort soll ich verwenden?")]
    [InlineData("Welches Kennwort soll in den Brief?")]
    [InlineData("Wie ist dein Firmenkennwort?")]
    [InlineData("Wie lautet die PIN zur Karte?")]
    [InlineData("Welche Geheimzahl hat die Karte?")]
    [InlineData("Trage das Kennwort unten ein.")]
    [InlineData("Bitte das Passwort ergänzen")]
    [InlineData("Please put the password below.")]
    [InlineData("What is the password for the account?")]
    [InlineData("Bitte schreiben Sie zur Bestätigung Ihr Passwort für das Firmenkonto in das Korrekturfeld unten.")]
    public void A_request_for_the_secret_itself_is_always_found(string text)
    {
        Assert.True(IntakeContentGuard.AsksForSecret(text), text);
        Assert.True(IntakeContentGuard.IsUnsafe(text, PasswordGuideRequest), text);
    }

    [Fact]
    public void Even_when_the_request_treats_passwords_a_question_for_the_password_is_dropped()
    {
        var result = Apply(PasswordGuideRequest, "de",
            QuestionGateTests.Ask(GapTopic.Facts, "Wie lautet das Passwort für das Firmenkonto?", ["Siehe Notiz", "Weiß ich nicht"], assumption: null),
            QuestionGateTests.Ask(GapTopic.Tone, "Soll der Ratgeber zu sicheren Passwörtern eher locker sein?", ["Locker", "Sachlich"]));

        Assert.Equal(GateDecision.DroppedUnsafeContent, result.Trace[0].Decision);
        Assert.Equal("Soll der Ratgeber zu sicheren Passwörtern eher locker sein?", Assert.Single(result.Questions).Text);
    }

    [Theory]
    [InlineData(BankRequest, "Bitte schreiben Sie zur Bestätigung Ihr Passwort für das Firmenkonto in das Korrekturfeld unten.")]
    [InlineData(PasswordGuideRequest, "Bitte schreiben Sie zur Bestätigung Ihr Passwort für das Firmenkonto in das Korrekturfeld unten.")]
    [InlineData(BankRequest, "Eine Nachricht an die Bank mit der neuen Kontonummer.")]
    public void A_summary_with_a_secret_is_not_shown(string request, string understanding) =>
        Assert.Equal(string.Empty, ApplyUnderstanding(request, understanding).Understanding);

    [Fact]
    public void A_summary_about_a_secret_topic_the_request_treats_is_kept()
    {
        const string understanding = "Ein Ratgeber zu sicheren Passwörtern für das Team.";

        Assert.Equal(understanding, ApplyUnderstanding(PasswordGuideRequest, understanding).Understanding);
    }

    [Fact]
    public void Assumptions_with_a_secret_word_follow_the_request()
    {
        IntakeGap[] gaps =
        [
            QuestionGateTests.Gap(GapTopic.Facts, GapResolution.Assume, "Der Ratgeber stellt drei Regeln für sichere Passwörter vor."),
            QuestionGateTests.Gap(GapTopic.Other, GapResolution.Assume, "Das Datum bleibt als Platzhalter."),
        ];

        Assert.Equal(["Das Datum bleibt als Platzhalter."], Apply(BankRequest, "de", gaps).Assumptions.Select(a => a.Text));
        Assert.Equal(2, Apply(PasswordGuideRequest, "de", gaps).Assumptions.Count);
    }

    [Theory]
    [InlineData("Wie lautet der Name deiner Ansprechpartnerin?", "de", new[] { "Frau Lenz", "Herr Abel" })]
    [InlineData("Soll der Text die Tannen im Garten erwähnen?", "de", new[] { "Ja", "Nein" })]
    [InlineData("Soll die Reise in den Libanon erwähnt werden?", "de", new[] { "Ja", "Nein" })]
    [InlineData("Welche Stichworte sollen in den Brief?", "de", new[] { "Termin", "Kosten" })]
    [InlineData("Soll der Hinweis an der Pinnwand erwähnt werden?", "de", new[] { "Ja", "Nein" })]
    [InlineData("Soll der Wohnungsschlüssel erwähnt werden?", "de", new[] { "Ja", "Nein" })]
    [InlineData("Should the jacket in the ad be tan or black?", "en", new[] { "Tan", "Black" })]
    [InlineData("Should we pin the post to the top of the page?", "en", new[] { "Yes", "No" })]
    [InlineData("Which spinach dish should the menu mention?", "en", new[] { "The soup", "The pie" })]
    public void Questions_without_a_secret_word_are_kept(string text, string language, string[] options)
    {
        Assert.False(IntakeContentGuard.MentionsSecret(text), text);
        var request = language == "de" ? BankRequest : "Write a short note to the bank.";

        Assert.Single(Apply(request, language, QuestionGateTests.Ask(GapTopic.Facts, text, options)).Questions);
    }

    [Theory]
    [InlineData("Firmenkennwort")]
    [InlineData("Passwortsicherheit")]
    [InlineData("Karten-PIN")]
    [InlineData("KartenPIN")]
    [InlineData("PIN-Code")]
    [InlineData("pushTAN")]
    [InlineData("SMS-TAN")]
    [InlineData("Geheimzahl")]
    [InlineData("passwords")]
    public void Secret_words_are_found_inside_compounds(string word) =>
        Assert.True(IntakeContentGuard.MentionsSecret(word), word);
}
