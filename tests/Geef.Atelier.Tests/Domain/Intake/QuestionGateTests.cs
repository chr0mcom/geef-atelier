using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

public sealed class QuestionGateTests
{
    private const string SparseRequest = "Schreib eine Einladung zum Sommerfest unseres Teams.";

    private static readonly IReadOnlyDictionary<GapTopic, MemoryAnswer> Nothing = new Dictionary<GapTopic, MemoryAnswer>();

    internal static IntakeGap Ask(
        GapTopic topic,
        string text = "Wer soll die Einladung bekommen?",
        string[]? options = null,
        bool userCanKnow = true,
        bool web = false,
        GapImpact impact = GapImpact.High,
        string? assumption = "Das ganze Team",
        AssumptionConfidence confidence = AssumptionConfidence.Unsure,
        int? defaultIndex = null,
        bool allowFreeText = true) =>
        new(topic, GapResolution.Ask, userCanKnow, web, impact, assumption, confidence,
            new ProposedQuestion(text, options ?? ["Nur das Team", "Team mit Familien", "Die ganze Firma"], defaultIndex, allowFreeText));

    internal static IntakeGap Gap(GapTopic topic, GapResolution resolution, string? assumption = null, AssumptionConfidence confidence = AssumptionConfidence.Sure) =>
        new(topic, resolution, true, false, GapImpact.High, assumption, confidence, null);

    internal static ClarifierOutput Output(IReadOnlyList<IntakeGap> gaps, string language = "de", bool isRevision = false,
        LengthScale scale = LengthScale.Standard, IReadOnlyList<string>? research = null) =>
        new("Einladung", language, "Eine Einladung zum Sommerfest des Teams.", isRevision, null, scale, gaps, research ?? []);

    private static QuestionGateResult Apply(
        IReadOnlyList<IntakeGap> gaps,
        string request = SparseRequest,
        string language = "de",
        bool revisionDetected = false,
        bool isRevision = false,
        IReadOnlyDictionary<GapTopic, MemoryAnswer>? known = null,
        int maxQuestions = 3,
        IReadOnlyList<string>? research = null) =>
        QuestionGate.Apply(new QuestionGateInput(
            Output(gaps, language, isRevision, research: research), request, false, revisionDetected, known ?? Nothing, maxQuestions));

    private static GateDecision Only(QuestionGateResult result) => Assert.Single(result.Trace).Decision;

    [Fact]
    public void A_good_question_is_asked_with_stable_ids()
    {
        var result = Apply([Ask(GapTopic.Audience, defaultIndex: 1)]);

        var question = Assert.Single(result.Questions);
        Assert.Equal("q1", question.Id);
        Assert.Equal(GapTopic.Audience, question.Slot);
        Assert.Equal(["o1", "o2", "o3"], question.Options.Select(o => o.Id));
        Assert.Equal("o2", question.DefaultOptionId);
        Assert.False(question.FromTemplate);
        Assert.True(question.AllowFreeText);
        Assert.Equal(GateDecision.Asked, Only(result));
        Assert.Empty(result.Assumptions);
    }

    [Fact]
    public void The_profile_answers_the_sender_whatever_the_model_says()
    {
        var known = new Dictionary<GapTopic, MemoryAnswer> { [GapTopic.Sender] = new(GapTopic.Sender, "Eva Muster, Teamleitung", "profile") };

        var result = Apply([Ask(GapTopic.Sender, "Wer unterschreibt die Einladung?")], known: known);

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.FromMemory, Only(result));
        Assert.Equal("Eva Muster, Teamleitung", Assert.Single(result.FromMemory).Value);
        Assert.Empty(result.Assumptions);
    }

    [Fact]
    public void The_profile_rule_covers_only_profile_topics()
    {
        var known = new Dictionary<GapTopic, MemoryAnswer> { [GapTopic.Sender] = new(GapTopic.Sender, "Eva", "profile") };

        var result = Apply([Ask(GapTopic.Audience)], known: known);

        Assert.Equal(GateDecision.Asked, Only(result));
        Assert.Empty(result.FromMemory);
    }

    [Fact]
    public void Derived_gaps_are_dropped_without_assumption()
    {
        var result = Apply([Gap(GapTopic.Audience, GapResolution.Derived, "Das Team")]);

        Assert.Equal(GateDecision.Derived, Only(result));
        Assert.Empty(result.Assumptions);
    }

    [Fact]
    public void Memory_without_a_profile_value_becomes_an_assumption()
    {
        var result = Apply([Gap(GapTopic.Signature, GapResolution.Memory)]);

        Assert.Equal(GateDecision.Assumed, Only(result));
        Assert.Equal(QuestionGate.NotSpecified(GapTopic.Signature), Assert.Single(result.Assumptions).Text);
    }

    [Fact]
    public void Memory_with_a_profile_value_is_answered_from_it()
    {
        var known = new Dictionary<GapTopic, MemoryAnswer> { [GapTopic.Signature] = new(GapTopic.Signature, "Viele Grüße, Eva", "profile") };

        var result = Apply([Gap(GapTopic.Signature, GapResolution.Memory)], known: known);

        Assert.Equal(GateDecision.FromMemory, Only(result));
        Assert.Single(result.FromMemory);
    }

    [Fact]
    public void Research_gaps_go_to_research_with_the_topic_when_the_model_named_none()
    {
        var gap = new IntakeGap(GapTopic.Facts, GapResolution.Research, false, true, GapImpact.High, null, AssumptionConfidence.Unsure,
            new ProposedQuestion("Wann wurde die Firma gegründet?", [], null, false));

        var result = Apply([gap]);

        Assert.Equal(GateDecision.ToResearch, Only(result));
        Assert.Equal("Facts: Wann wurde die Firma gegründet?", Assert.Single(result.ResearchTopics));
    }

    [Fact]
    public void The_research_topics_of_the_model_are_kept_and_not_extended()
    {
        var result = Apply([Gap(GapTopic.Facts, GapResolution.Research)], research: ["Gründungsjahr Muster GmbH"]);

        Assert.Equal(["Gründungsjahr Muster GmbH"], result.ResearchTopics);
    }

    [Fact]
    public void Assume_gaps_keep_their_assumption()
    {
        var result = Apply([Gap(GapTopic.Tone, GapResolution.Assume, "Locker und herzlich", AssumptionConfidence.Unsure)]);

        Assert.Equal(GateDecision.Assumed, Only(result));
        var assumption = Assert.Single(result.Assumptions);
        Assert.Equal(("a1", GapTopic.Tone, "Locker und herzlich", AssumptionConfidence.Unsure),
            (assumption.Id, assumption.Topic, assumption.Text, assumption.Confidence));
    }

    [Fact]
    public void Researchable_questions_are_never_asked()
    {
        var result = Apply([Ask(GapTopic.Facts, "Wie viele Mitarbeiter hat die Firma?", ["Unter 50", "50 bis 250", "Über 250"], web: true)]);

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.ToResearch, Only(result));
        Assert.Single(result.ResearchTopics);
    }

    [Fact]
    public void Questions_the_person_cannot_answer_are_dropped_and_keep_the_assumption()
    {
        var result = Apply([Ask(GapTopic.Format, userCanKnow: false, assumption: "Fließtext mit Absätzen")]);

        Assert.Equal(GateDecision.DroppedUserCannotKnow, Only(result));
        Assert.Equal("Fließtext mit Absätzen", Assert.Single(result.Assumptions).Text);
    }

    [Fact]
    public void Low_impact_questions_are_dropped()
    {
        var result = Apply([Ask(GapTopic.Format, impact: GapImpact.Low)]);

        Assert.Equal(GateDecision.DroppedLowImpact, Only(result));
        Assert.Single(result.Assumptions);
    }

    [Theory]
    [InlineData(GapTopic.Length)]
    [InlineData(GapTopic.Tone)]
    [InlineData(GapTopic.Formality)]
    public void A_revision_never_asks_about_length_tone_or_formality(GapTopic topic)
    {
        var result = Apply([Ask(topic, "Wie förmlich soll der Text klingen?", ["Förmlich", "Locker"])], revisionDetected: true);

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedRevisionRule, Only(result));
    }

    [Fact]
    public void A_revision_flagged_by_the_model_counts_as_well()
    {
        var result = Apply([Ask(GapTopic.Tone, "Wie soll der Text klingen?", ["Förmlich", "Locker"])], isRevision: true);

        Assert.Equal(GateDecision.DroppedRevisionRule, Only(result));
    }

    [Fact]
    public void A_revision_asks_at_most_one_question()
    {
        var result = Apply(
        [
            Ask(GapTopic.Audience),
            Ask(GapTopic.Purpose, "Wozu soll der Text dienen?", ["Informieren", "Überzeugen"]),
        ], revisionDetected: true);

        Assert.Single(result.Questions);
        Assert.Equal([GateDecision.Asked, GateDecision.DroppedOverLimit], result.Trace.Select(t => t.Decision));
    }

    [Fact]
    public void An_open_length_is_asked_with_the_german_template()
    {
        var result = Apply([Ask(GapTopic.Length, "egal", [], assumption: "Etwa 150 Wörter")]);

        var question = Assert.Single(result.Questions);
        Assert.Equal(QuestionTemplates.LengthQuestionId, question.Id);
        Assert.Equal(QuestionTemplates.GermanLengthQuestion, question.Text);
        Assert.True(question.FromTemplate);
        Assert.True(question.AllowFreeText);
        Assert.Null(question.DefaultOptionId);
        Assert.Equal(["short", "medium", "long"], question.Options.Select(o => o.Id));
        Assert.Equal("Kurz (etwa 150 Wörter)", question.Options[0].Label);
    }

    [Fact]
    public void The_english_template_follows_the_length_scale()
    {
        var gaps = new[] { Ask(GapTopic.Length, "", []) };
        var result = QuestionGate.Apply(new QuestionGateInput(
            Output(gaps, "en", scale: LengthScale.LongForm), "Write a report about our year.", false, false, Nothing, 3));

        var question = Assert.Single(result.Questions);
        Assert.Equal(QuestionTemplates.EnglishLengthQuestion, question.Text);
        Assert.Equal("Compact (about 800 words)", question.Options[0].Label);
    }

    [Theory]
    [InlineData("Schreib eine Einladung zum Sommerfest, etwa 200 Wörter.")]
    [InlineData("Schreib eine Einladung zum Sommerfest in 300 Wörtern.")]
    [InlineData("Schreib eine kurze Einladung zum Sommerfest, höchstens 1000 Zeichen.")]
    public void A_length_in_the_request_means_no_length_question(string request)
    {
        var result = Apply([Ask(GapTopic.Length, "", [], assumption: null)], request: request);

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedLengthKnown, Only(result));
        Assert.Empty(result.Assumptions);
    }

    [Fact]
    public void A_sure_length_is_not_asked()
    {
        var result = Apply([Ask(GapTopic.Length, "", [], assumption: "Etwa 150 Wörter", confidence: AssumptionConfidence.Sure)]);

        Assert.Equal(GateDecision.DroppedLengthSure, Only(result));
        Assert.Equal("Etwa 150 Wörter", Assert.Single(result.Assumptions).Text);
    }

    [Fact]
    public void French_gets_no_length_question_but_an_assumption()
    {
        var result = Apply([Ask(GapTopic.Length, "", [], assumption: "Environ 150 mots")], request: "Écris une invitation pour la fête d'été.", language: "fr");

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedLengthNoTemplate, Only(result));
        Assert.Equal("Environ 150 mots", Assert.Single(result.Assumptions).Text);
    }

    [Fact]
    public void A_question_of_121_characters_is_dropped_and_becomes_an_assumption()
    {
        var text = new string('x', 120) + "?";
        Assert.Equal(121, text.Length);

        var result = Apply([Ask(GapTopic.Audience, text, assumption: null)]);

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedTooLong, Only(result));
        Assert.Equal(QuestionGate.NotSpecified(GapTopic.Audience), Assert.Single(result.Assumptions).Text);
    }

    [Fact]
    public void A_question_of_exactly_120_characters_is_asked()
    {
        var text = new string('x', 119) + "?";

        Assert.Equal(GateDecision.Asked, Only(Apply([Ask(GapTopic.Audience, text)])));
    }

    [Fact]
    public void An_ask_without_question_is_dropped()
    {
        var gap = Ask(GapTopic.Audience) with { Question = null };

        Assert.Equal(GateDecision.DroppedTooLong, Only(Apply([gap])));
    }

    [Theory]
    [InlineData("Nur das Team")]
    [InlineData("A|B|C|D|E")]
    [InlineData("Nur das Team| ")]
    [InlineData("Nur das Team|nur das team")]
    [InlineData("Nur das Team|egal")]
    [InlineData("Nur das Team|Other (please say)")]
    [InlineData("Nur das Team|Sonstiges")]
    public void Bad_options_drop_the_question(string options)
    {
        var result = Apply([Ask(GapTopic.Audience, options: options.Split('|'))]);

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedBadOptions, Only(result));
    }

    [Fact]
    public void An_option_over_60_characters_drops_the_question()
    {
        var result = Apply([Ask(GapTopic.Audience, options: ["Nur das Team", new string('y', 61)])]);

        Assert.Equal(GateDecision.DroppedBadOptions, Only(result));
    }

    [Fact]
    public void Two_and_four_options_are_fine()
    {
        Assert.Equal(GateDecision.Asked, Only(Apply([Ask(GapTopic.Audience, options: ["Ja", "Nein"])])));
        Assert.Equal(GateDecision.Asked, Only(Apply([Ask(GapTopic.Audience, options: ["A", "B", "C", "D"])])));
    }

    [Fact]
    public void A_jargon_word_in_the_question_drops_it()
    {
        var result = Apply([Ask(GapTopic.Audience, "Wer ist die Zielgruppe der Einladung?")]);

        Assert.Empty(result.Questions);
        Assert.Equal(GateDecision.DroppedJargon, Only(result));
        Assert.Single(result.Assumptions);
    }

    [Fact]
    public void A_jargon_word_in_an_option_drops_the_question()
    {
        var result = Apply([Ask(GapTopic.Tone, "Wie soll die Einladung klingen?", ["Locker", "Mit klarem Call to Action"])]);

        Assert.Equal(GateDecision.DroppedJargon, Only(result));
    }

    [Fact]
    public void A_question_in_another_language_than_the_text_is_dropped()
    {
        var english = "Who is this invitation for, and what should the people who read it do after they have read it?";
        var result = Apply([Ask(GapTopic.Audience, english, ["The team only", "The team and their families", "The whole company"])]);

        Assert.Equal(GateDecision.DroppedLanguageMismatch, Only(result));
    }

    [Fact]
    public void A_short_question_without_a_certain_language_guess_is_asked()
    {
        var result = Apply([Ask(GapTopic.Audience, "Who is it for?", ["Team", "Families"])]);

        Assert.Equal(GateDecision.Asked, Only(result));
    }

    [Fact]
    public void Two_questions_about_the_same_topic_become_one()
    {
        var result = Apply(
        [
            Ask(GapTopic.Audience),
            Ask(GapTopic.Audience, "Sollen auch Familien kommen?", ["Ja", "Nein"], assumption: "Ohne Familien"),
        ]);

        Assert.Single(result.Questions);
        Assert.Equal([GateDecision.Asked, GateDecision.DroppedDuplicateTopic], result.Trace.Select(t => t.Decision));
        Assert.Equal("Ohne Familien", Assert.Single(result.Assumptions).Text);
    }

    [Fact]
    public void Four_questions_become_three_with_length_first()
    {
        var result = Apply(
        [
            Ask(GapTopic.Audience),
            Ask(GapTopic.Purpose, "Wozu soll die Einladung dienen?", ["Informieren", "Zum Kommen bewegen"]),
            Ask(GapTopic.Date, "Steht das Datum schon fest?", ["Ja", "Nein"]),
            Ask(GapTopic.Length, "", [], assumption: "Etwa 150 Wörter"),
        ]);

        Assert.Equal(["length", "q1", "q2"], result.Questions.Select(q => q.Id));
        Assert.Equal([GapTopic.Length, GapTopic.Audience, GapTopic.Purpose], result.Questions.Select(q => q.Slot));
        Assert.Equal(
            [GateDecision.Asked, GateDecision.Asked, GateDecision.DroppedOverLimit, GateDecision.Asked],
            result.Trace.Select(t => t.Decision));
        Assert.Equal([0, 1, 2, 3], result.Trace.Select(t => t.GapIndex));
    }

    [Fact]
    public void The_configured_limit_is_capped_at_three()
    {
        var gaps = Enumerable.Range(0, 5)
            .Select(i => Ask((GapTopic)(i + 1), $"Frage Nummer {i}?", ["Ja", "Nein"]))
            .ToList();

        Assert.Equal(3, Apply(gaps, maxQuestions: 10).Questions.Count);
        Assert.Single(Apply(gaps, maxQuestions: 1).Questions);
        Assert.Empty(Apply(gaps, maxQuestions: 0).Questions);
    }

    [Fact]
    public void Assumptions_are_capped_at_eight_with_the_unsure_ones_first()
    {
        var gaps = Enumerable.Range(0, 10)
            .Select(i => Gap(GapTopic.Other, GapResolution.Assume, $"Annahme {i}", i % 2 == 0 ? AssumptionConfidence.Sure : AssumptionConfidence.Unsure))
            .ToList();

        var result = Apply(gaps);

        Assert.Equal(QuestionGate.MaxAssumptions, result.Assumptions.Count);
        Assert.Equal(Enumerable.Range(1, 8).Select(i => $"a{i}"), result.Assumptions.Select(a => a.Id));
        Assert.All(result.Assumptions.Take(5), a => Assert.Equal(AssumptionConfidence.Unsure, a.Confidence));
        Assert.Equal("Annahme 1", result.Assumptions[0].Text);
        Assert.Equal("Annahme 0", result.Assumptions[5].Text);
    }

    [Fact]
    public void Equal_assumptions_are_shown_once()
    {
        var result = Apply([Gap(GapTopic.Tone, GapResolution.Assume, "Locker"), Gap(GapTopic.Formality, GapResolution.Assume, "locker")]);

        Assert.Single(result.Assumptions);
    }

    [Fact]
    public void An_invalid_default_index_gives_no_default_option()
    {
        Assert.Null(Assert.Single(Apply([Ask(GapTopic.Audience, defaultIndex: 3)]).Questions).DefaultOptionId);
        Assert.Null(Assert.Single(Apply([Ask(GapTopic.Audience, defaultIndex: -1)]).Questions).DefaultOptionId);
    }

    [Fact]
    public void Free_text_follows_the_model()
    {
        Assert.False(Assert.Single(Apply([Ask(GapTopic.Audience, allowFreeText: false)]).Questions).AllowFreeText);
    }

    [Fact]
    public void Question_text_and_options_are_trimmed()
    {
        var question = Assert.Single(Apply([Ask(GapTopic.Audience, "  Für wen ist die Einladung?  ", ["  Team ", " Familien"])]).Questions);

        Assert.Equal("Für wen ist die Einladung?", question.Text);
        Assert.Equal(["Team", "Familien"], question.Options.Select(o => o.Label));
    }

    [Fact]
    public void Every_gap_gets_one_trace_entry()
    {
        var gaps = new[]
        {
            Gap(GapTopic.Audience, GapResolution.Derived),
            Gap(GapTopic.Tone, GapResolution.Assume, "Locker"),
            Ask(GapTopic.Purpose, "Wozu dient sie?", ["Informieren", "Einladen"]),
        };

        var result = Apply(gaps);

        Assert.Equal(gaps.Select(g => g.Topic), result.Trace.Select(t => t.Topic));
        Assert.Equal([GateDecision.Derived, GateDecision.Assumed, GateDecision.Asked], result.Trace.Select(t => t.Decision));
    }

    [Fact]
    public void Random_outputs_never_break_the_question_rule()
    {
        var random = new Random(20261125);
        var topics = Enum.GetValues<GapTopic>();
        var resolutions = Enum.GetValues<GapResolution>();
        var words = new[] { "Wer", "soll", "die", "Einladung", "bekommen", "prompt", "Zielgruppe", "egal", "the", "and", "Team", "run", "x" };
        string Words(int max) => string.Join(' ', Enumerable.Range(0, random.Next(0, max)).Select(_ => words[random.Next(words.Length)]));

        for (var n = 0; n < 10_000; n++)
        {
            var gaps = Enumerable.Range(0, random.Next(0, 9)).Select(_ => new IntakeGap(
                topics[random.Next(topics.Length)],
                resolutions[random.Next(resolutions.Length)],
                random.Next(2) == 0,
                random.Next(4) == 0,
                random.Next(3) == 0 ? GapImpact.Low : GapImpact.High,
                random.Next(3) == 0 ? null : Words(6),
                random.Next(2) == 0 ? AssumptionConfidence.Sure : AssumptionConfidence.Unsure,
                random.Next(5) == 0 ? null : new ProposedQuestion(
                    random.Next(10) == 0 ? new string('a', random.Next(100, 200)) : Words(30),
                    Enumerable.Range(0, random.Next(0, 7)).Select(_ => random.Next(15) == 0 ? new string('o', 70) : Words(4)).ToList(),
                    random.Next(-1, 5),
                    random.Next(2) == 0))).ToList();
            var language = random.Next(5) switch { 0 => "en", 1 => "fr", _ => "de" };
            var output = new ClarifierOutput("Text", language, "Verständnis", random.Next(8) == 0, null,
                (LengthScale)random.Next(3), gaps, []);
            var request = random.Next(6) == 0 ? "Bitte etwa 200 Wörter." : "Schreib etwas.";

            var result = QuestionGate.Apply(new QuestionGateInput(output, request, false, random.Next(10) == 0, Nothing, random.Next(0, 6)));

            Assert.True(result.Questions.Count <= QuestionGate.HardMaxQuestions);
            Assert.Equal(gaps.Count, result.Trace.Count);
            Assert.True(result.Assumptions.Count <= QuestionGate.MaxAssumptions);
            Assert.Equal(result.Questions.Count, result.Questions.Select(q => q.Slot).Distinct().Count());
            Assert.Equal(result.Questions.Count, result.Questions.Select(q => q.Id).Distinct().Count());
            foreach (var question in result.Questions)
            {
                Assert.InRange(question.Text.Length, 1, QuestionGate.MaxQuestionChars);
                Assert.InRange(question.Options.Count, QuestionGate.MinOptions, QuestionGate.MaxOptions);
                Assert.All(question.Options, o => Assert.InRange(o.Label.Length, 1, QuestionGate.MaxOptionChars));
                Assert.False(JargonBlocklist.Contains(question.Text));
                Assert.All(question.Options, o => Assert.False(JargonBlocklist.Contains(o.Label) || JargonBlocklist.IsFillerOption(o.Label)));
            }
        }
    }
}
