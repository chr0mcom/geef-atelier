using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary>
/// Review round 1 of stage 2: lengths named in pages, sentences or as a tweet count as given; a length question under another
/// topic is the template question; links, addresses and requests for access data never reach the person.
/// </summary>
public sealed class QuestionGateSafetyAndLengthTests
{
    private static readonly IReadOnlyDictionary<GapTopic, MemoryAnswer> Nothing = new Dictionary<GapTopic, MemoryAnswer>();

    private static QuestionGateResult Apply(string request, string language, params IntakeGap[] gaps) =>
        QuestionGate.Apply(new QuestionGateInput(QuestionGateTests.Output(gaps, language), request, false, Nothing, 3));

    private static QuestionGateResult Apply(ClarifierOutput output, string request = "Schreib eine Einladung zum Sommerfest.") =>
        QuestionGate.Apply(new QuestionGateInput(output, request, false, Nothing, 3));

    [Theory]
    [InlineData("Schreib eine halbe Seite über unser Sommerfest.", "de")]
    [InlineData("Schreib in 3 Sätzen eine Absage an den Bewerber.", "de")]
    [InlineData("Schreib zwei Absätze über das neue Büro.", "de")]
    [InlineData("Write a one-page summary of the board meeting.", "en")]
    [InlineData("Write two paragraphs about our new office.", "en")]
    [InlineData("Write a tweet about our summer party.", "en")]
    [InlineData("Write half a page on the new opening hours.", "en")]
    public void A_length_in_pages_sentences_paragraphs_or_a_tweet_counts_as_given(string request, string language)
    {
        var result = Apply(request, language, QuestionGateTests.Ask(GapTopic.Length, "", []));

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedLengthKnown, Assert.Single(result.Trace).Decision);
    }

    [Theory]
    [InlineData("Schreib eine Seitenleiste für die Website.")]
    [InlineData("Write a landing page text for our bakery.")]
    [InlineData("Schreib eine Einladung zum Sommerfest.")]
    public void Without_a_named_length_the_length_question_stays(string request)
    {
        Assert.False(LengthMentions.InRequest(request));
        var result = Apply(request, "de", QuestionGateTests.Ask(GapTopic.Length, "", []));

        Assert.Equal(QuestionTemplates.LengthQuestionId, Assert.Single(result.Questions).Id);
    }

    [Fact]
    public void A_length_question_under_format_or_scope_becomes_the_single_template_question()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de",
            QuestionGateTests.Ask(GapTopic.Format, "Wie lang soll der Beitrag werden?", ["Kurz", "Mittel", "Lang"]),
            QuestionGateTests.Ask(GapTopic.Scope, "Welchen Umfang soll der Text haben?", ["Knapp", "Ausführlich"]),
            QuestionGateTests.Ask(GapTopic.Audience));

        Assert.Equal([QuestionTemplates.LengthQuestionId, "q1"], result.Questions.Select(q => q.Id));
        Assert.True(result.Questions[0].FromTemplate);
        Assert.Equal(GapTopic.Audience, result.Questions[1].Slot);
        Assert.Equal(GateDecision.DroppedDuplicateTopic, result.Trace[1].Decision);
    }

    [Fact]
    public void A_length_question_under_another_topic_is_dropped_when_the_request_names_a_length()
    {
        var result = Apply("Write about 200 words about our summer party.", "en",
            QuestionGateTests.Ask(GapTopic.Scope, "How detailed and long should it be?", ["Short", "Detailed"]));

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedLengthKnown, Assert.Single(result.Trace).Decision);
    }

    [Fact]
    public void Options_with_word_counts_make_a_length_question()
    {
        Assert.True(LengthMentions.AsksForLength(new ProposedQuestion("Was passt?", ["150 Wörter", "400 Wörter"], null, false)));
        Assert.False(LengthMentions.AsksForLength(new ProposedQuestion("Wer kommt?", ["Das Team", "Alle"], null, false)));
    }

    public static TheoryData<string, string[]> UnsafeQuestions() => new()
    {
        { "Wie ist dein Passwort für das Firmenkonto?", ["Siehe Notiz", "Weiß ich nicht"] },
        { "Welches Konto soll ich nehmen?", ["Siehe www.evil.example/login", "Das alte"] },
        { "Please confirm your login at https://evil.example/x", ["Done", "Later"] },
        { "Wohin soll die Antwort gehen?", ["an chef@example.org", "an das Team"] },
        { "Welche Seite ist gemeint?", ["[Anmelden](https://evil.example/login)", "Die alte"] },
        { "Bitte nenne die TAN für die Überweisung.", ["Gleich", "Später"] },
        { "What is your card number?", ["I'll send it", "Skip"] },
        { "Gib bitte deine IBAN an.", ["Mach ich", "Nein"] },
        { "Which API key should I use?", ["The test key", "Your key"] },
        { "Welche Seite: evil-login.example?", ["Ja", "Nein"] },
    };

    [Theory]
    [MemberData(nameof(UnsafeQuestions))]
    public void Questions_and_options_with_links_addresses_or_requests_for_secrets_are_dropped(string text, string[] options)
    {
        var language = text.StartsWith("Wh", StringComparison.Ordinal) || text.StartsWith("Pl", StringComparison.Ordinal) ? "en" : "de";
        var result = Apply("Schreib eine kurze Nachricht an die Bank.", language, QuestionGateTests.Ask(GapTopic.Facts, text, options, assumption: null));

        Assert.Empty(result.Questions);
        Assert.Contains(Assert.Single(result.Trace).Decision, new[] { GateDecision.DroppedUnsafeContent, GateDecision.DroppedJargon });
        Assert.All(result.Assumptions, a => Assert.False(IntakeContentGuard.IsUnsafe(a.Text)));
    }

    [Theory]
    [InlineData("Soll der Text über Passwortsicherheit eher locker sein?", "de")]
    [InlineData("Für wen ist der Ratgeber zu sicheren Passwörtern?", "de")]
    [InlineData("Should the guide explain how a strong password looks?", "en")]
    [InlineData("Soll ich notes.docx als Grundlage nehmen?", "de")]
    public void A_question_about_a_text_on_password_safety_or_a_file_name_is_kept(string text, string language)
    {
        var result = Apply("Schreib einen Ratgeber zu Passwortsicherheit für unser Team.", language,
            QuestionGateTests.Ask(GapTopic.Tone, text, language == "de" ? ["Locker", "Sachlich"] : ["Casual", "Plain"]));

        Assert.Single(result.Questions);
    }

    [Fact]
    public void Assumptions_with_links_or_requests_for_secrets_are_dropped_and_safe_ones_stay()
    {
        var result = Apply("Schreib eine Einladung zum Sommerfest.", "de",
            QuestionGateTests.Gap(GapTopic.Other, GapResolution.Assume, "Bitte zuerst anmelden: [Anmelden](https://evil.example/login) und das Kennwort senden."),
            QuestionGateTests.Gap(GapTopic.Facts, GapResolution.Assume, "Schick mir dein Passwort, dann geht es schneller."),
            QuestionGateTests.Gap(GapTopic.Date, GapResolution.Assume, "Das Datum bleibt als Platzhalter."),
            QuestionGateTests.Gap(GapTopic.Format, GapResolution.Assume, "Antworten an info@example.com."));

        Assert.Equal(["Das Datum bleibt als Platzhalter."], result.Assumptions.Select(a => a.Text));
    }

    [Theory]
    [InlineData("Eine Einladung, Anmeldung über https://evil.example/x.")]
    [InlineData("Eine Einladung; siehe [hier](http://evil.example).")]
    [InlineData("Eine Einladung an info@example.com.")]
    public void An_understanding_with_a_link_or_address_is_not_shown(string understanding)
    {
        var output = QuestionGateTests.Output([]) with { Understanding = understanding };

        Assert.Equal(string.Empty, Apply(output).Understanding);
    }

    [Fact]
    public void A_plain_understanding_is_kept()
    {
        var output = QuestionGateTests.Output([]);

        Assert.Equal(output.Understanding, Apply(output).Understanding);
    }

    [Theory]
    [InlineData("Ein Brief an die Firma z.B. mit Datum.", false)]
    [InlineData("Version 2.0 des Plans", false)]
    [InlineData("notes.docx und plan.pdf", false)]
    [InlineData("www.example.org", true)]
    [InlineData("example.com", true)]
    [InlineData("ftp://host/file", true)]
    public void Links_and_domains_are_found_but_file_names_and_abbreviations_are_not(string text, bool expected) =>
        Assert.Equal(expected, IntakeContentGuard.HasLinkOrAddress(text));
}
