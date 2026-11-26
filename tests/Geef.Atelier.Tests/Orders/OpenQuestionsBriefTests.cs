using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Microsoft.Extensions.Options;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// Skipping, "You decide" and starting without answers never commit the person: a question the person left open, and a question about a
/// possible decision that the gate did not ask, reach the writer only under the heading that forbids deciding it for them, never as the
/// guess of the clarifier or as "the writer decides", and no question carries a preselected answer. Answers of the person, answers of
/// the assistant and the fixed length question stay exactly as before. The example is made up.
/// </summary>
public sealed class OpenQuestionsBriefTests
{
    private const string Request = "Antworte der Schule auf den Elternbrief zur Klassenfahrt im Anhang.";
    private const string Question = "Darf dein Kind an der Klassenfahrt teilnehmen?";
    private const string SecondQuestion = "Übernimmst du den Elternbeitrag für den Ausflug?";
    private const string Guess = "Das Kind nimmt an der Klassenfahrt teil.";
    private const string SecondGuess = "Der Elternbeitrag wird übernommen.";
    private static readonly string[] TripOptions = ["Ja, es fährt mit", "Nein, es fährt nicht mit", "Ich brauche noch Bedenkzeit"];
    private static readonly OrderEntity TheOrder = Order("user-a", Request);

    [Fact]
    public void The_gate_keeps_the_decision_question_without_a_preselected_answer_and_without_the_guess()
    {
        var gate = Gate(DecisionGap(Question, TripOptions, Guess), FormGap());

        var question = Assert.Single(gate.Questions);
        Assert.Equal(Question, question.Text);
        Assert.Equal(TripOptions, question.Options.Select(o => o.Label));
        Assert.Null(question.DefaultOptionId);
        Assert.DoesNotContain(gate.Assumptions, a => a.Text == Guess);
        Assert.Null(gate.HeldBack);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_skipped_decision_reaches_the_writer_only_as_an_open_question(bool shown)
    {
        var text = Compose(Record(DecisionGap(Question, TripOptions, Guess), FormGap()), [], shown);

        Assert.Equal(
            Request + BriefingSections.Separator + IntakeBriefComposer.OpenQuestionsHeading + "\n- " + Question + "\n" +
            (shown ? IntakeBriefComposer.AssumptionsHeading : IntakeBriefComposer.AssumptionsNotShownHeading) + "\n- Förmliches Sie.",
            text);
        Assert.DoesNotContain("fährt mit", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Guess, text, StringComparison.Ordinal);
        Assert.DoesNotContain(IntakeBriefComposer.AnswersHeading, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AnswerOrigin.User)]
    [InlineData(AnswerOrigin.Client)]
    public void You_decide_is_an_open_question_and_never_left_to_the_writer_as_an_answer(AnswerOrigin origin)
    {
        var text = Compose(Record(DecisionGap(Question, TripOptions, Guess), FormGap()), [new OrderAnswer("q1", null, null, true, origin)]);

        Assert.Contains(IntakeBriefComposer.OpenQuestionsHeading + "\n- " + Question + "\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(IntakeBriefComposer.LeftToYou, text, StringComparison.Ordinal);
        Assert.DoesNotContain(IntakeBriefComposer.AnswersHeading, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Answers_left_open_by_answering_only_other_points_are_open_questions()
    {
        var record = Record(DecisionGap(Question, TripOptions, Guess), FormGap());
        var answers = OrderAnswerRules.Normalize(record, [new OrderAnswer("a1", null, "Du statt Sie", false, AnswerOrigin.User)], 500);

        var text = Compose(record, answers);

        Assert.Contains(IntakeBriefComposer.CorrectionsHeading + "\n- Förmliches Sie. → Du statt Sie\n" + IntakeBriefComposer.OpenQuestionsHeading + "\n- " + Question,
            text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("o2", null, AnswerOrigin.User, "Nein, es fährt nicht mit")]
    [InlineData("o3", null, AnswerOrigin.Client, "Ich brauche noch Bedenkzeit (answered by the assistant)")]
    [InlineData(null, "Ja, aber erst ab dem zweiten Tag", AnswerOrigin.User, "\"Ja, aber erst ab dem zweiten Tag\"")]
    public void An_answer_of_the_person_or_the_assistant_stays_exactly_as_before(string? option, string? free, AnswerOrigin origin, string expected)
    {
        var record = Record(DecisionGap(Question, TripOptions, Guess, allowFreeText: true), FormGap());

        var text = Compose(record, [new OrderAnswer("q1", option, free, false, origin)]);

        Assert.Equal(
            Request + BriefingSections.Separator + IntakeBriefComposer.AnswersHeading + "\n- " + Question + " → " + expected + "\n" +
            IntakeBriefComposer.AssumptionsHeading + "\n- Förmliches Sie.",
            text);
        Assert.DoesNotContain(IntakeBriefComposer.OpenQuestionsHeading, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_fixed_length_question_never_opens_the_block()
    {
        var length = QuestionTemplates.Length("de", LengthScale.ShortForm);
        var record = Record(FormGap()) with { Questions = [length] };

        Assert.Equal(Request + BriefingSections.Separator + IntakeBriefComposer.AssumptionsHeading + "\n- Förmliches Sie.", Compose(record, []));
        Assert.Contains("→ " + IntakeBriefComposer.LeftToYou, Compose(record, [new OrderAnswer("length", null, null, true, AnswerOrigin.User)]), StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_decision_on_the_same_topic_is_held_back_and_open_never_lost()
    {
        var record = Record(DecisionGap(Question, TripOptions, Guess), DecisionGap(SecondQuestion, ["Ja", "Nein"], SecondGuess), FormGap());

        Assert.Equal([SecondQuestion], record.HeldBack!);
        Assert.Contains(record.Trace, t => t.Decision == GateDecision.DroppedDuplicateTopic);
        var text = Compose(record, []);
        Assert.Contains(IntakeBriefComposer.OpenQuestionsHeading + "\n- " + Question + "\n- " + SecondQuestion + "\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondGuess, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("o1")]
    [InlineData("o2")]
    public void A_held_back_copy_of_an_answered_question_is_not_open(string option)
    {
        var copy = "darf dein Kind an der Klassenfahrt teilnehmen ?!";
        var record = Record(DecisionGap(Question, TripOptions, Guess), DecisionGap(copy, ["Ja", "Nein"], SecondGuess), FormGap());
        Assert.Equal([copy.Trim()], record.HeldBack!);

        var text = Compose(record, [new OrderAnswer("q1", option, null, false, AnswerOrigin.User)]);

        Assert.DoesNotContain(IntakeBriefComposer.OpenQuestionsHeading, text, StringComparison.Ordinal);
        Assert.Contains(IntakeBriefComposer.AnswersHeading + "\n- " + Question + " → ", text, StringComparison.Ordinal);
        Assert.Empty(BriefingSections.Parse(text).OpenQuestions!);
        Assert.Equal(Question.SameQuestionKey(), copy.SameQuestionKey());
    }

    [Fact]
    public void A_held_back_question_in_other_words_stays_open_under_the_rule_that_the_answer_of_the_person_wins()
    {
        var other = "Soll dein Kind bei der Klassenfahrt mitfahren?";
        var record = Record(DecisionGap(Question, TripOptions, Guess), DecisionGap(other, ["Ja", "Nein"], SecondGuess), FormGap());

        var text = Compose(record, [new OrderAnswer("q1", "o2", null, false, AnswerOrigin.User)]);

        Assert.Contains(IntakeBriefComposer.AnswersHeading + "\n- " + Question + " → Nein, es fährt nicht mit\n", text, StringComparison.Ordinal);
        Assert.Contains(IntakeBriefComposer.OpenQuestionsHeading + "\n- " + other + "\n", text, StringComparison.Ordinal);
        Assert.True(text.IndexOf(IntakeBriefComposer.AnswersHeading, StringComparison.Ordinal) < text.IndexOf(IntakeBriefComposer.OpenQuestionsHeading, StringComparison.Ordinal));
        Assert.Contains("the answers and corrections of the person", IntakeBriefComposer.OpenQuestionsHeading, StringComparison.Ordinal);
        Assert.DoesNotContain(SecondGuess, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decision_over_the_limit_of_a_revision_is_held_back_and_its_guess_is_no_assumption()
    {
        var audience = Geef.Atelier.Tests.Domain.Intake.QuestionGateTests.Ask(GapTopic.Audience, "Wer liest die Antwort?", ["Die Lehrerin", "Die Schulleitung"]);
        var output = Output(audience, DecisionGap(Question, TripOptions, Guess), FormGap()) with { IsRevision = true };
        var gate = QuestionGate.Apply(new QuestionGateInput(output, Request, true, new Dictionary<GapTopic, MemoryAnswer>(), 3));
        var record = Record(gate, output);

        Assert.Equal("Wer liest die Antwort?", Assert.Single(gate.Questions).Text);
        Assert.Equal(GateDecision.DroppedOverLimit, gate.Trace[1].Decision);
        Assert.Equal([Question], gate.HeldBack!);
        var text = Compose(record, [new OrderAnswer("q1", "o1", null, false, AnswerOrigin.User)], shown: false);
        Assert.Contains(IntakeBriefComposer.OpenQuestionsHeading + "\n- " + Question + "\n", text, StringComparison.Ordinal);
        Assert.Contains("- Wer liest die Antwort? → Die Lehrerin\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Guess, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decision_dropped_for_its_options_is_held_back_and_never_left_to_the_writer()
    {
        var record = Record(DecisionGap(Question, ["Ja, es fährt mit", "Du entscheidest"], Guess), FormGap());

        Assert.Empty(record.Questions);
        Assert.Equal(GateDecision.DroppedBadOptions, record.Trace[0].Decision);
        var text = Compose(record, [], shown: false);
        Assert.Contains(IntakeBriefComposer.OpenQuestionsHeading + "\n- " + Question + "\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("not specified", text, StringComparison.Ordinal);
        Assert.DoesNotContain("the writer decides", text, StringComparison.Ordinal);
        Assert.DoesNotContain(Guess, text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_decision_without_a_question_text_is_held_back_under_its_topic()
    {
        var gap = DecisionGap(Question, TripOptions, Guess) with { Question = null };

        var gate = Gate(gap, FormGap());

        Assert.Equal(["Purpose: not asked"], gate.HeldBack!);
        Assert.DoesNotContain(gate.Assumptions, a => a.Text.Contains("not specified", StringComparison.Ordinal) || a.Text == Guess);
    }

    [Theory]
    [InlineData(GapTopic.Facts)]
    [InlineData(GapTopic.Audience)]
    public void Dropped_questions_on_other_topics_keep_their_assumption_as_before(GapTopic topic)
    {
        var gate = Gate(DecisionGap(Question, ["Ja", "Du entscheidest"], Guess) with { Topic = topic }, FormGap());

        Assert.Null(gate.HeldBack);
        Assert.Contains(gate.Assumptions, a => a.Text == Guess);
    }

    [Fact]
    public void A_record_without_held_back_questions_serialises_as_before()
    {
        var record = Record(DecisionGap(Question, TripOptions, Guess), FormGap());

        var json = IntakeRecordJson.Serialize(record);

        Assert.DoesNotContain("heldBack", json, StringComparison.Ordinal);
        Assert.Null(IntakeRecordJson.TryParse(json)!.HeldBack);
        var held = Record(DecisionGap(Question, TripOptions, Guess), DecisionGap(SecondQuestion, ["Ja", "Nein"], SecondGuess), FormGap());
        Assert.Equal([SecondQuestion], IntakeRecordJson.TryParse(IntakeRecordJson.Serialize(held))!.HeldBack!);
    }

    [Fact]
    public void An_unchecked_start_of_an_intake_order_gets_the_general_rule_and_nothing_else()
    {
        foreach (var record in new[] { null, IntakeRecord.Fallback(IntakeRecordStatus.TimedOut, "timeout", 40_000, null, BriefMode.AppendAnswers) })
        {
            var brief = Composer().Compose(new OrderBriefInput(TheOrder, record, [], null, NotChecked: true));

            Assert.Equal(Request + BriefingSections.Separator + IntakeBriefComposer.NotCheckedHeading, brief.Text);
            Assert.Equal([IntakeBriefComposer.RequestSection, IntakeBriefComposer.NotCheckedSection], brief.Sections);
            Assert.Equal(Request, BriefingSections.RequestPart(brief.Text));
        }
        Assert.Equal(Request, Composer().Compose(new OrderBriefInput(TheOrder, null, [], null)).Text);
        var checkedRecord = Record(FormGap());
        Assert.Equal(Compose(checkedRecord, []), Composer().Compose(new OrderBriefInput(TheOrder, checkedRecord, [], null, AssumptionsShown: true, NotChecked: true)).Text);
    }

    [Fact]
    public void The_headings_forbid_deciding_for_the_person_and_let_the_person_win()
    {
        foreach (var heading in new[] { IntakeBriefComposer.OpenQuestionsHeading, IntakeBriefComposer.NotCheckedHeading })
        {
            Assert.DoesNotContain('\n', heading);
            foreach (var part in new[]
                     {
                         "never decide it for the person", "accept or decline", "order, cancel, pay or confirm attendance",
                         "confirming receipt, asking back or asking for time", "placeholder in square brackets",
                         "name the open decision among your open points",
                     })
                Assert.Contains(part, heading, StringComparison.Ordinal);
        }
        Assert.Contains("Where the request or a change the person asks for settles it, follow the person.", IntakeBriefComposer.NotCheckedHeading, StringComparison.Ordinal);
        Assert.Contains(
            "The request, the answers and corrections of the person and any change the person asks for take precedence: where one of them settles one of these questions, follow the person. Otherwise, where the answer would commit the person",
            IntakeBriefComposer.OpenQuestionsHeading, StringComparison.Ordinal);
        Assert.EndsWith(":", IntakeBriefComposer.OpenQuestionsHeading, StringComparison.Ordinal);
        Assert.Contains("Never invent facts", IntakeBriefComposer.OpenQuestionsHeading, StringComparison.Ordinal);
        Assert.Contains("choose what fits the request best", IntakeBriefComposer.OpenQuestionsHeading, StringComparison.Ordinal);
        Assert.Contains("or was not asked", IntakeBriefComposer.OpenQuestionsHeading, StringComparison.Ordinal);
    }

    [Fact]
    public void Questions_in_the_record_count_as_open_only_without_a_usable_answer()
    {
        var question = Record(DecisionGap(Question, TripOptions, Guess), FormGap()).Questions[0];
        Dictionary<string, OrderAnswer> Answers(params OrderAnswer[] given) => given.ToDictionary(a => a.QuestionId, StringComparer.Ordinal);

        Assert.True(question.IsLeftOpen(Answers()));
        Assert.True(question.IsLeftOpen(Answers(new OrderAnswer("q1", null, null, true, AnswerOrigin.User))));
        Assert.True(question.IsLeftOpen(Answers(new OrderAnswer("q1", "o9", null, false, AnswerOrigin.User))));
        Assert.True(question.IsLeftOpen(Answers(new OrderAnswer("q1", null, " ", false, AnswerOrigin.User))));
        Assert.False(question.IsLeftOpen(Answers(new OrderAnswer("q1", "o1", null, false, AnswerOrigin.User))));
        Assert.False(question.IsLeftOpen(Answers(new OrderAnswer("q1", null, "Nein", false, AnswerOrigin.Client))));
    }

    private static IntakeBriefComposer Composer() => new(Options.Create(new IntakeOptions { BriefMode = BriefMode.AppendAnswers }));

    private static string Compose(IntakeRecord record, IReadOnlyList<OrderAnswer> answers, bool shown = true) =>
        Composer().Compose(new OrderBriefInput(TheOrder, record, answers, null, AssumptionsShown: shown)).Text;

    private static IntakeGap DecisionGap(string text, string[] options, string guess, bool allowFreeText = false) =>
        new(GapTopic.Purpose, GapResolution.Ask, true, false, GapImpact.High, guess, AssumptionConfidence.Unsure,
            new ProposedQuestion(text, options, 0, allowFreeText));

    private static IntakeGap FormGap() =>
        new(GapTopic.Formality, GapResolution.Assume, true, false, GapImpact.Low, "Förmliches Sie.", AssumptionConfidence.Sure, null);

    private static ClarifierOutput Output(params IntakeGap[] gaps) =>
        new("E-Mail", "de", "Eine kurze Antwort an die Schule zur Klassenfahrt.", false, null, LengthScale.ShortForm, gaps, []);

    private static QuestionGateResult Gate(params IntakeGap[] gaps) =>
        QuestionGate.Apply(new QuestionGateInput(Output(gaps), Request, false, new Dictionary<GapTopic, MemoryAnswer>(), 3));

    private static IntakeRecord Record(params IntakeGap[] gaps) => Record(Gate(gaps), Output(gaps));

    private static IntakeRecord Record(QuestionGateResult gate, ClarifierOutput output) =>
        new(IntakeRecord.CurrentSchemaVersion, IntakeRecordStatus.Ok, "test-model", 1_000, null, output, gate.Trace,
            gate.Questions, gate.Assumptions, gate.FromMemory, gate.ResearchTopics, null, BriefMode.AppendAnswers, gate.HeldBack);
}
