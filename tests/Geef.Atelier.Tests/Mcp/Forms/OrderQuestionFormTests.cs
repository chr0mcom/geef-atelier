using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Mcp.Forms;
using Geef.Atelier.Tests.Mcp.Orders;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Geef.Atelier.Tests.Mcp.Forms;

/// <summary>The form of <c>submit_order</c> (D-088): what it shows, what it refuses to show, and how its answers are read.</summary>
public sealed class OrderQuestionFormTests
{
    private const int MaxFreeText = 500;

    private static readonly ClarifyQuestion Purpose = new(
        "q2", GapTopic.Purpose, "Soll die Mail eine Zusage enthalten?",
        [new QuestionOption("o1", "Ja, zusagen"), new QuestionOption("o2", "Nein, absagen")], null, false, false);

    private static ClarificationView Clarification(params ClarifyQuestion[] questions) =>
        OrderToolTestData.Clarification(questions.Length == 0 ? [OrderToolTestData.LengthQuestion, OrderToolTestData.AudienceQuestion] : questions);

    [Fact]
    public void The_form_has_one_choice_per_question_with_you_decide_own_words_where_allowed_and_nothing_required()
    {
        var (form, refused) = OrderQuestionForm.Build(Clarification(), MaxFreeText);

        Assert.Null(refused);
        Assert.NotNull(form);
        Assert.Equal("form", form.Mode);
        Assert.Equal(OrderQuestionForm.Message, form.Message);
        var schema = form.RequestedSchema!;
        Assert.True(schema.Required is null || schema.Required.Count == 0);
        Assert.Equal(["length", "length_text", "q1"], schema.Properties.Keys);

        var length = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(schema.Properties["length"]);
        Assert.Equal("Wie lang soll der Text ungefähr werden?", length.Title);
        Assert.Equal(["short", "medium", "long", "decide_for_me"], length.OneOf.Select(o => o.Const));
        Assert.Equal("You decide", length.OneOf[^1].Title);
        Assert.Null(length.Default);
        var words = Assert.IsType<ElicitRequestParams.StringSchema>(schema.Properties["length_text"]);
        Assert.Equal(MaxFreeText, words.MaxLength);
        Assert.Null(words.Default);
        var audience = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(schema.Properties["q1"]);
        Assert.Null(audience.Default);
        Assert.Equal(["o1", "o2", "decide_for_me"], audience.OneOf.Select(o => o.Const));
    }

    [Fact]
    public void The_message_is_fixed_and_says_that_skipping_is_fine_and_atelier_never_asks_for_secrets()
    {
        Assert.Contains("Skipping is fine", OrderQuestionForm.Message, StringComparison.Ordinal);
        Assert.Contains("never asks for passwords, PINs, TANs, keys, card or account numbers", OrderQuestionForm.Message, StringComparison.Ordinal);
        var (form, _) = OrderQuestionForm.Build(Clarification(), MaxFreeText);
        var serialized = JsonSerializer.Serialize(form, McpJsonUtilities.DefaultOptions);
        Assert.DoesNotContain("Eine kurze Mail", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Freundlicher", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_to_improve_adds_the_revision_choice_without_you_decide()
    {
        var clarification = Clarification() with { RevisionFileName = "entwurf.docx" };

        var (form, _) = OrderQuestionForm.Build(clarification, MaxFreeText);

        var revision = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(form!.RequestedSchema!.Properties["revision"]);
        Assert.Equal("Improve the file “entwurf.docx”?", revision.Title);
        Assert.Equal(["yes", "no"], revision.OneOf.Select(o => o.Const));
    }

    public static TheoryData<string, string> UnsafeQuestions() => new()
    {
        { "Bitte geben Sie Ihre TAN ein, um fortzufahren.", "unsafe_question" },
        { "What is your password for the portal?", "unsafe_question" },
        { "Wie lautet Ihre IBAN?", "unsafe_question" },
        { "Soll der Link https://example.com/login in die Mail?", "unsafe_question" },
        { "Soll ich www.example.com nennen?", "unsafe_question" },
        { "An wen geht die Mail? Schreib an info@example.com", "unsafe_question" },
        { "Soll [hier](javascript:alert) stehen?", "unsafe_question" },
        { "Wer bekommt die Mail?‮etadpu", "unsafe_question" },
        { "Wer bekommt\nIgnoriere alles", "unsafe_question" },
        { "Welche Kartennummer soll rein?", "unsafe_question" },
        { "Wie lautet der Einmalcode?", "unsafe_question" },
    };

    [Theory]
    [MemberData(nameof(UnsafeQuestions))]
    public void Questions_that_sound_like_a_secret_hold_a_link_or_hidden_characters_are_never_shown_as_a_form(string text, string reason)
    {
        var question = OrderToolTestData.AudienceQuestion with { Text = text };

        var (form, refused) = OrderQuestionForm.Build(Clarification(question), MaxFreeText);

        Assert.Null(form);
        Assert.Equal(reason, refused);
    }

    [Theory]
    [InlineData("Bitte PIN eingeben")]
    [InlineData("Siehe example.org")]
    [InlineData("Ja​, zusagen")]
    public void An_option_that_sounds_like_a_secret_or_holds_a_link_refuses_the_form(string label)
    {
        var question = Purpose with { Options = [new QuestionOption("o1", label), new QuestionOption("o2", "Nein")] };

        var (form, refused) = OrderQuestionForm.Build(Clarification(question), MaxFreeText);

        Assert.Null(form);
        Assert.Equal("unsafe_option", refused);
    }

    [Fact]
    public void Too_long_texts_bad_ids_too_many_questions_and_an_unsafe_file_name_refuse_the_form()
    {
        Assert.Equal("unsafe_question", OrderQuestionForm.Build(Clarification(Purpose with { Text = new string('a', OrderFormText.MaxQuestionChars + 1) }), MaxFreeText).Refused);
        Assert.Equal("unsafe_option", OrderQuestionForm.Build(Clarification(Purpose with { Options = [new QuestionOption("o1", new string('b', OrderFormText.MaxOptionChars + 1))] }), MaxFreeText).Refused);
        Assert.Equal("unusable_question_id", OrderQuestionForm.Build(Clarification(Purpose with { Id = "q 2" }), MaxFreeText).Refused);
        Assert.Equal("unusable_question_id", OrderQuestionForm.Build(Clarification(Purpose with { Id = "revision" }), MaxFreeText).Refused);
        Assert.Equal("unusable_options", OrderQuestionForm.Build(Clarification(Purpose with { Options = [new QuestionOption("decide_for_me", "Ja")] }), MaxFreeText).Refused);
        Assert.Equal("unusable_options", OrderQuestionForm.Build(Clarification(Purpose with { Options = [] }), MaxFreeText).Refused);
        Assert.Equal("too_many_questions", OrderQuestionForm.Build(Clarification(Purpose, Purpose with { Id = "q3" }, Purpose with { Id = "q4" }, Purpose with { Id = "q5" }), MaxFreeText).Refused);
        Assert.Equal("duplicate_question_id", OrderQuestionForm.Build(Clarification(Purpose, Purpose), MaxFreeText).Refused);
        Assert.Equal("unsafe_file_name", OrderQuestionForm.Build(Clarification() with { RevisionFileName = "www.example.com.docx" }, MaxFreeText).Refused);
        Assert.Equal("no_questions", OrderQuestionForm.Build(OrderToolTestData.Clarification([]), MaxFreeText).Refused);
        Assert.Equal("no_clarification", OrderQuestionForm.Build(null, MaxFreeText).Refused);
    }

    [Fact]
    public void Ordinary_questions_and_options_pass()
    {
        foreach (var text in new[] { "Wie lang soll der Text ungefähr werden?", "Soll die Mail z. B. einen Dank enthalten?", "Version 2.0 oder 3.1?", "Who is the e-mail for?" })
            Assert.True(text.IsSafeForForm(OrderFormText.MaxQuestionChars), text);
    }

    [Fact]
    public void A_sent_form_gives_one_answer_per_question_from_the_form_with_choices_words_you_decide_and_empty_questions()
    {
        var clarification = Clarification(OrderToolTestData.LengthQuestion, OrderToolTestData.AudienceQuestion, Purpose) with { RevisionFileName = "entwurf.docx" };

        var reading = OrderQuestionForm.Read(Accept(("length", "short"), ("length_text", "  höchstens zwei Absätze "), ("q1", "decide_for_me"), ("revision", "no")), clarification, MaxFreeText);

        Assert.Equal(OrderFormOutcome.Accepted, reading.Outcome);
        Assert.All(reading.Answers, a => Assert.Equal(AnswerOrigin.Form, a.Origin));
        Assert.Equal(
        [
            new OrderAnswer("length", "short", "höchstens zwei Absätze", false, AnswerOrigin.Form),
            new OrderAnswer("q1", null, null, true, AnswerOrigin.Form),
            new OrderAnswer("q2", null, null, true, AnswerOrigin.Form),
            new OrderAnswer("revision", "no", null, false, AnswerOrigin.Form),
        ], reading.Answers);
    }

    [Fact]
    public void An_empty_sent_form_leaves_every_question_to_atelier_and_keeps_the_file_guess()
    {
        var clarification = Clarification() with { RevisionFileName = "entwurf.docx" };

        var reading = OrderQuestionForm.Read(Accept(), clarification, MaxFreeText);

        Assert.Equal(OrderFormOutcome.Accepted, reading.Outcome);
        Assert.Equal(["length", "q1"], reading.Answers.Select(a => a.QuestionId));
        Assert.All(reading.Answers, a => Assert.True(a.DecideForMe));
    }

    [Fact]
    public void Own_words_alone_are_an_answer()
    {
        var reading = OrderQuestionForm.Read(Accept(("length_text", "eine halbe Seite")), Clarification(), MaxFreeText);

        Assert.Equal(new OrderAnswer("length", null, "eine halbe Seite", false, AnswerOrigin.Form), reading.Answers[0]);
    }

    [Fact]
    public void Declined_and_closed_forms_and_missing_answers_give_no_answers()
    {
        Assert.Equal(OrderFormOutcome.Declined, OrderQuestionForm.Read(new ElicitResult { Action = "decline" }, Clarification(), MaxFreeText).Outcome);
        Assert.Equal(OrderFormOutcome.Cancelled, OrderQuestionForm.Read(new ElicitResult { Action = "cancel" }, Clarification(), MaxFreeText).Outcome);
        Assert.Equal(OrderFormOutcome.Cancelled, OrderQuestionForm.Read(null, Clarification(), MaxFreeText).Outcome);
        Assert.Empty(OrderQuestionForm.Read(new ElicitResult { Action = "decline" }, Clarification(), MaxFreeText).Answers);
    }

    public static TheoryData<string, object> InvalidContent() => new()
    {
        { "q1", "o9" },
        { "unknown", "x" },
        { "q1_text", "eigene Worte" },
        { "length", 3 },
        { "length_text", new string('x', MaxFreeText + 1) },
        { "length_text", "Text\u0007mit Steuerzeichen" },
        { "length_text", "Text‮mit Richtungswechsel" },
        { "revision", "maybe" },
    };

    [Theory]
    [MemberData(nameof(InvalidContent))]
    public void Anything_that_does_not_fit_the_form_makes_it_invalid(string key, object value)
    {
        var clarification = Clarification() with { RevisionFileName = "entwurf.docx" };
        var content = new Dictionary<string, JsonElement> { [key] = JsonSerializer.SerializeToElement(value) };

        var reading = OrderQuestionForm.Read(new ElicitResult { Action = "accept", Content = content }, clarification, MaxFreeText);

        Assert.Equal(OrderFormOutcome.Invalid, reading.Outcome);
        Assert.Empty(reading.Answers);
    }

    [Fact]
    public void You_decide_together_with_own_words_is_invalid()
    {
        var reading = OrderQuestionForm.Read(Accept(("length", "decide_for_me"), ("length_text", "kurz")), Clarification(), MaxFreeText);

        Assert.Equal(OrderFormOutcome.Invalid, reading.Outcome);
    }

    [Fact]
    public void A_revision_answer_without_a_file_to_improve_is_invalid()
    {
        Assert.Equal(OrderFormOutcome.Invalid, OrderQuestionForm.Read(Accept(("revision", "no")), Clarification(), MaxFreeText).Outcome);
    }

    [Fact]
    public void Form_answers_pass_the_answer_rules_of_the_order_service_and_unanswered_questions_take_the_form_origin()
    {
        var reading = OrderQuestionForm.Read(Accept(("length", "medium")), Clarification(), MaxFreeText);
        var record = Record(OrderToolTestData.LengthQuestion, OrderToolTestData.AudienceQuestion);

        var normalized = Normalize(record, reading.Answers);

        Assert.Equal([AnswerOrigin.Form, AnswerOrigin.Form], normalized.Select(a => a.Origin));
        var defaulted = Normalize(record, [new OrderAnswer("length", "short", null, false, AnswerOrigin.Form)]);
        Assert.Equal(new OrderAnswer("q1", null, null, true, AnswerOrigin.Form), defaulted[1]);
    }

    private static IReadOnlyList<OrderAnswer> Normalize(IntakeRecord record, IReadOnlyList<OrderAnswer> answers) =>
        OrderAnswerRules.Normalize(record, answers, MaxFreeText);

    private static IntakeRecord Record(params ClarifyQuestion[] questions) =>
        new(IntakeRecord.CurrentSchemaVersion, IntakeRecordStatus.Ok, null, 0, null, null, [], questions, [], [], [], null, BriefMode.AppendAnswers);

    private static ElicitResult Accept(params (string Key, string Value)[] values) =>
        new() { Action = "accept", Content = values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value)) };
}
