using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// Review round 3 of stage 2: a length named anywhere in the request keeps the length question away, but never makes a length check
/// of the text stricter; validity periods are no text length, "short or long" about the text is; a broad topic keeps the assumption
/// of a dropped duplicate; "pin" after an owner word is a secret; a request about payment data never allows access data, and back.
/// </summary>
public sealed class QuestionGateReviewRound3Tests
{
    private static readonly IReadOnlyDictionary<GapTopic, MemoryAnswer> Nothing = new Dictionary<GapTopic, MemoryAnswer>();

    private static QuestionGateResult Apply(string request, string language, params IntakeGap[] gaps) =>
        QuestionGate.Apply(new QuestionGateInput(QuestionGateTests.Output(gaps, language), request, false, Nothing, 3));

    public static TheoryData<string, string> LengthsOfTheReview() => new()
    {
        { "Kannst du mir zwei Absätze über unser Sommerfest schreiben?", "de" },
        { "Ich hätte gern eine halbe Seite über das Fest.", "de" },
        { "Schreib eine Absage. Maximal drei Sätze.", "de" },
        { "Der Brief soll höchstens eine Seite lang sein.", "de" },
        { "Schreib mir bitte eine kurze Mail, 3 Sätze reichen.", "de" },
        { "Schreib uns einen Text, etwa eine Seite.", "de" },
        { "Write an email to Bob. Keep it to three sentences.", "en" },
        { "Keep it under one page: a summary of our offer.", "en" },
        { "I'd like two paragraphs about our bakery.", "en" },
        { "Can you write me a short two-paragraph bio?", "en" },
        { "Write a 2-page report on the project.", "en" },
    };

    [Theory]
    [MemberData(nameof(LengthsOfTheReview))]
    public void A_length_named_anywhere_in_the_request_is_not_asked_again(string request, string language)
    {
        var result = Apply(request, language, QuestionGateTests.Ask(GapTopic.Length, "", []));

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedLengthKnown, Assert.Single(result.Trace).Decision);
    }

    [Fact]
    public void A_unit_in_another_meaning_suppresses_the_length_question_on_purpose()
    {
        var result = Apply("Im Buch fehlen zehn Seiten. Schreib eine Reklamation an den Verlag.", "de",
            QuestionGateTests.Ask(GapTopic.Length, "", [], assumption: "Eine kurze, sachliche Reklamation"));

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedLengthKnown, Assert.Single(result.Trace).Decision);
        Assert.Contains(result.Assumptions, a => a.Text == "Eine kurze, sachliche Reklamation");
    }

    public static TheoryData<string> BroadOnlyLengths() =>
    [
        .. LengthMentionsCorpusTests.Named.Concat(LengthMentionsCorpusTests.DeliberatelyCounted)
            .Where(r => RequestConstraintExtractor.Extract(r).Length is null),
    ];

    [Theory]
    [MemberData(nameof(BroadOnlyLengths))]
    public void The_broad_detection_only_drops_the_question_and_never_adds_a_length_check(string request)
    {
        Assert.True(LengthMentions.InRequest(request), request);
        var result = Apply(request, "de", QuestionGateTests.Ask(GapTopic.Length, "", []));
        var record = new IntakeRecord(
            IntakeRecord.CurrentSchemaVersion, IntakeRecordStatus.Ok, "model", 1000, null, QuestionGateTests.Output([], "de"),
            result.Trace, result.Questions, result.Assumptions, result.FromMemory, result.ResearchTopics, null, BriefMode.AppendAnswers);
        var answer = new OrderAnswer(QuestionTemplates.LengthQuestionId, "short", null, false, AnswerOrigin.User);

        var constraints = IntakeConstraints.WithAnswers(RequestConstraintExtractor.Extract(request), record, [answer]);

        Assert.Equal(GateDecision.DroppedLengthKnown, Assert.Single(result.Trace).Decision);
        Assert.Null(constraints.Length);
        Assert.Null(RequestConstraintExtractor.Extract(request).Length);
    }

    [Fact]
    public void The_set_of_requests_that_only_the_broad_detection_finds_is_not_empty() =>
        Assert.True(BroadOnlyLengths().Count >= 30, BroadOnlyLengths().Count.ToString());

    [Theory]
    [InlineData("Wie lange soll die Einladung gültig sein?")]
    [InlineData("Wie lange soll die Mail-Aktion laufen?")]
    [InlineData("How long should the reply window be?")]
    [InlineData("How long would you like the offer in the letter to stay valid?")]
    public void A_question_how_long_something_stays_valid_or_runs_is_no_text_length(string text)
    {
        Assert.False(LengthMentions.AsksForTextLength(new ProposedQuestion(text, ["Eine Woche", "Einen Monat"], null, false)), text);

        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de", QuestionGateTests.Ask(GapTopic.Other, text, ["Eine Woche", "Einen Monat"]));

        Assert.False(Assert.Single(result.Questions).FromTemplate);
    }

    [Theory]
    [InlineData("Soll das Schreiben kurz oder lang sein?", "de", new[] { "Kurz", "Lang" })]
    [InlineData("Do you want a short or a long version?", "en", new[] { "Short", "Long" })]
    public void Short_or_long_about_the_text_is_the_length_template(string text, string language, string[] options)
    {
        Assert.True(LengthMentions.AsksForTextLength(new ProposedQuestion(text, options, null, false)), text);

        var result = Apply(language == "de" ? "Schreib eine Einladung zum Sommerfest." : "Write an invitation to our summer party.", language,
            QuestionGateTests.Ask(GapTopic.Length, "", []),
            QuestionGateTests.Ask(GapTopic.Format, text, options));

        Assert.Equal(QuestionTemplates.LengthQuestionId, Assert.Single(result.Questions).Id);
    }

    [Fact]
    public void A_second_different_fact_question_keeps_the_assumption_the_model_gave()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de",
            QuestionGateTests.Ask(GapTopic.Facts, "Wann beginnt das Fest?", ["Am Mittag", "Am Abend"], assumption: "Beginn am Nachmittag"),
            QuestionGateTests.Ask(GapTopic.Facts, "Wo findet das Fest statt?", ["Im Garten", "Im Saal"], assumption: "Der Ort bleibt als Platzhalter offen"));

        Assert.Equal("Wann beginnt das Fest?", Assert.Single(result.Questions).Text);
        Assert.Equal(GateDecision.DroppedDuplicateTopic, result.Trace[1].Decision);
        Assert.Contains(result.Assumptions, a => a.Text == "Der Ort bleibt als Platzhalter offen");
        Assert.DoesNotContain(result.Assumptions, a => a.Text == "Beginn am Nachmittag");
    }

    [Fact]
    public void A_dropped_fact_duplicate_without_a_model_assumption_gets_no_not_specified_line()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de",
            QuestionGateTests.Ask(GapTopic.Facts, "Wann beginnt das Fest?", ["Am Mittag", "Am Abend"]),
            QuestionGateTests.Ask(GapTopic.Facts, "Wo findet das Fest statt?", ["Im Garten", "Im Saal"], assumption: null));

        Assert.DoesNotContain(result.Assumptions, a => a.Text == QuestionGate.NotSpecified(GapTopic.Facts));
    }

    [Theory]
    [InlineData("Wie lautet dein Pin?")]
    [InlineData("Gib deine Pin ein.")]
    [InlineData("Please enter your pin.")]
    [InlineData("What is your pin?")]
    [InlineData("Wie lautet Ihre neue Pin?")]
    [InlineData("What is your bank pin?")]
    public void Pin_in_any_case_after_an_owner_word_is_a_secret(string text)
    {
        Assert.True(IntakeContentGuard.MentionsSecret(text), text);
        Assert.True(IntakeContentGuard.IsUnsafe(text, "Schreib eine kurze Nachricht an die Bank."), text);
    }

    [Theory]
    [InlineData("Should we pin the post to the top of the page?")]
    [InlineData("Soll der Hinweis an der Pinnwand erwähnt werden?")]
    [InlineData("Should the pin on the map show the main entrance?")]
    public void Pin_without_an_owner_word_is_no_secret(string text) =>
        Assert.False(IntakeContentGuard.MentionsSecret(text), text);

    [Fact]
    public void A_request_about_payment_data_never_allows_access_data()
    {
        const string request = "Schreib eine Mahnung mit meiner IBAN.";

        Assert.True(IntakeContentGuard.IsUnsafe("Passwort fürs Online-Banking?", request));
        Assert.False(IntakeContentGuard.IsUnsafe("Soll die IBAN in die Fußzeile?", request));
        Assert.False(IntakeContentGuard.IsUnsafe("Soll die Kontonummer neben der IBAN stehen?", request));

        var result = Apply(request, "de", QuestionGateTests.Ask(GapTopic.Facts, "Passwort fürs Online-Banking?", ["Ja", "Nein"]));
        Assert.Equal(GateDecision.DroppedUnsafeContent, Assert.Single(result.Trace).Decision);
    }

    [Fact]
    public void A_request_about_access_data_never_allows_payment_data()
    {
        const string request = "Schreib einen Ratgeber zu Passwortsicherheit für unser Team.";

        Assert.True(IntakeContentGuard.IsUnsafe("Soll die IBAN des Vereins mit hinein?", request));
        Assert.False(IntakeContentGuard.IsUnsafe("Soll der Ratgeber auch die PIN der Bankkarte erwähnen?", request));
    }

    [Fact]
    public void A_request_about_both_kinds_allows_both_but_never_a_request_for_the_secret()
    {
        const string request = "Schreib einen Ratgeber zu sicheren Passwörtern und zum Schutz der IBAN.";

        Assert.False(IntakeContentGuard.IsUnsafe("Soll die IBAN im Beispiel geschwärzt sein und das Passwort-Beispiel erfunden?", request));
        Assert.True(IntakeContentGuard.IsUnsafe("Wie lautet dein Passwort?", request));
    }

    [Theory]
    [InlineData("Sollen Platzhalter wie [Name][Datum] bleiben?")]
    [InlineData("Bleiben [Vorname] und [Nachname] als Platzhalter?")]
    public void Placeholders_in_brackets_are_no_link(string text) =>
        Assert.False(IntakeContentGuard.HasLinkOrAddress(text, "Schreib eine Einladung zum Sommerfest."), text);

    [Theory]
    [InlineData("Siehe [Anmelden][1]")]
    [InlineData("Siehe [Anmelden][]")]
    [InlineData("Besuche evil.com_")]
    [InlineData("Besuche evil.com_login")]
    public void Reference_links_and_a_domain_before_an_underscore_are_links(string text) =>
        Assert.True(IntakeContentGuard.HasLinkOrAddress(text, "Schreib eine Einladung zum Sommerfest."), text);
}
