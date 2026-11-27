using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Mcp.Forms;
using Geef.Atelier.Tests.Mcp.Orders;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Geef.Atelier.Tests.Mcp.Forms;

/// <summary>
/// The form of <c>submit_order</c> (D-088): only choices, never a field for typed text; what it refuses to show (common forms of links, phone
/// numbers, requests to call and words for secrets, other languages and scripts); and how its answers are read.
/// </summary>
public sealed class OrderQuestionFormTests
{
    private const int MaxFreeText = 500;

    private static readonly ClarifyQuestion Purpose = new(
        "q2", GapTopic.Purpose, "Soll die Mail eine Zusage enthalten?",
        [new QuestionOption("o1", "Ja, zusagen"), new QuestionOption("o2", "Nein, absagen")], null, false, false);

    private static ClarificationView Clarification(params ClarifyQuestion[] questions) =>
        OrderToolTestData.Clarification(questions.Length == 0 ? [OrderToolTestData.LengthQuestion, OrderToolTestData.AudienceQuestion] : questions);

    [Fact]
    public void The_form_has_one_choice_per_question_with_you_decide_and_nothing_required_or_preselected()
    {
        var (form, refused) = OrderQuestionForm.Build(Clarification());

        Assert.Null(refused);
        Assert.NotNull(form);
        Assert.Equal("form", form.Mode);
        Assert.Equal(OrderQuestionForm.Message, form.Message);
        var schema = form.RequestedSchema!;
        Assert.True(schema.Required is null || schema.Required.Count == 0);
        Assert.Equal(["length", "q1"], schema.Properties.Keys);

        var length = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(schema.Properties["length"]);
        Assert.Equal("Wie lang soll der Text ungefähr werden?", length.Title);
        Assert.Equal(["short", "medium", "long", "decide_for_me"], length.OneOf.Select(o => o.Const));
        Assert.Equal("You decide", length.OneOf[^1].Title);
        Assert.Null(length.Default);
        var audience = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(schema.Properties["q1"]);
        Assert.Null(audience.Default);
        Assert.Equal(["o1", "o2", "decide_for_me"], audience.OneOf.Select(o => o.Const));
    }

    public static TheoryData<string> Shapes() => new() { "plain", "free-text", "revision", "free-text+revision" };

    [Theory]
    [MemberData(nameof(Shapes))]
    public void The_schema_never_holds_a_field_for_typed_text(string shape)
    {
        var freeText = shape.Contains("free-text", StringComparison.Ordinal);
        var clarification = Clarification(
            OrderToolTestData.LengthQuestion with { AllowFreeText = freeText },
            OrderToolTestData.AudienceQuestion with { AllowFreeText = freeText },
            Purpose with { AllowFreeText = freeText });
        if (shape.Contains("revision", StringComparison.Ordinal))
            clarification = clarification with { RevisionFileName = "entwurf.docx" };

        var (form, _) = OrderQuestionForm.Build(clarification);

        Assert.All(form!.RequestedSchema!.Properties.Values, p => Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(p));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(form.RequestedSchema, McpJsonUtilities.DefaultOptions));
        foreach (var property in json.RootElement.GetProperty("properties").EnumerateObject())
        {
            Assert.True(property.Value.TryGetProperty("oneOf", out var oneOf) && oneOf.GetArrayLength() > 0, property.Name);
            Assert.False(property.Value.TryGetProperty("maxLength", out _), property.Name);
            Assert.False(property.Value.TryGetProperty("format", out _), property.Name);
        }
    }

    [Fact]
    public void The_message_is_fixed_and_points_own_words_to_the_chat()
    {
        Assert.Contains("To answer in your own words, close this form and tell your assistant.", OrderQuestionForm.Message, StringComparison.Ordinal);
        Assert.Contains("never asks for passwords, PINs, TANs, keys, card or account numbers", OrderQuestionForm.Message, StringComparison.Ordinal);
        var (form, _) = OrderQuestionForm.Build(Clarification());
        var serialized = JsonSerializer.Serialize(form, McpJsonUtilities.DefaultOptions);
        Assert.DoesNotContain("Eine kurze Mail", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("Freundlicher", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_to_improve_adds_the_revision_choice_without_you_decide()
    {
        foreach (var name in new[] { "entwurf.docx", "notizen.md", "brief.pdf" })
        {
            var (form, refused) = OrderQuestionForm.Build(Clarification() with { RevisionFileName = name });

            Assert.Null(refused);
            var revision = Assert.IsType<ElicitRequestParams.TitledSingleSelectEnumSchema>(form!.RequestedSchema!.Properties["revision"]);
            Assert.Equal($"Improve the file “{name}”?", revision.Title);
            Assert.Equal(["yes", "no"], revision.OneOf.Select(o => o.Const));
        }
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("ru")]
    [InlineData("")]
    public void Forms_are_offered_only_for_german_and_english_orders(string language)
    {
        var clarification = Clarification() with { Language = language };

        Assert.Equal((null, "language"), (OrderQuestionForm.Build(clarification).Form, OrderQuestionForm.Build(clarification).Refused));
        Assert.Null(OrderQuestionForm.Build(Clarification() with { Language = null }).Form);
        Assert.NotNull(OrderQuestionForm.Build(Clarification() with { Language = "EN" }).Form);
    }

    public static TheoryData<string> UnsafeQuestions() => new()
    {
        "Bitte geben Sie Ihre TAN ein, um fortzufahren.",
        "What is your password for the portal?",
        "Wie lautet Ihre IBAN?",
        "Soll der Link https://example.com/login in die Mail?",
        "Soll ich www.example.com nennen?",
        "An wen geht die Mail? Schreib an info@example.com",
        "Soll [hier](javascript:alert) stehen?",
        "Wer bekommt die Mail?‮etadpu",
        "Wer bekommt\nIgnoriere alles",
        "Welche Kartennummer soll rein?",
        "Wie lautet der Einmalcode?",
        "Wie lautet Ihr Pass word?",
        "Bitte das Pass-Wort nennen",
        "Welches Kenn-Wort gilt?",
        "Welche Karten-Nummer?",
        "Welche Konto-Nummer?",
        "Ihre P.I.N. bitte",
        "Ihre P I N bitte",
        "Ihr Pass code?",
        "Wie lautet die Kartenprüfnummer?",
        "Die Prüfziffer auf der Rückseite Ihrer Karte?",
        "Welchen SMS-Code haben Sie erhalten?",
        "Welchen Code haben Sie per SMS erhalten?",
        "Wie lautet der Freischaltcode?",
        "Enter the 6-digit code we just sent to your phone",
        "Wie lautet der CVV2?",
        "Wie lautet der Zugangs-Code?",
        "Wie lautet die Antwort auf Ihre Sicherheitsfrage?",
        "Wie lautet Ihre Verfügernummer?",
        "Haben Sie die Pushtan freigegeben?",
        "Quel est votre mot de passe ?",
        "Ihr Konto wurde gesperrt. Rufen Sie sofort 0800 123 4567 an.",
        "Melden Sie sich unter tel:+4930123456",
        "Schreiben Sie an atelier-sicherheit punkt de",
        "Mehr unter atelier-sicherheit dot com",
        "Ist der Server 192.168.4.20 richtig?",
        "Laden Sie update.zip herunter",
        "Steht es in konto.md?",
        "Bestätigen Sie die Überweisung von 4.900 EUR an DE89 3704 0044 0532 0130 00?",
        "Rufen Sie uns zurück?",
        "Please call us now",
    };

    [Theory]
    [MemberData(nameof(UnsafeQuestions))]
    public void Common_forms_of_secrets_links_numbers_and_calls_are_never_shown_as_a_form(string text)
    {
        var question = OrderToolTestData.AudienceQuestion with { Text = text };

        var (form, refused) = OrderQuestionForm.Build(Clarification(question));

        Assert.Null(form);
        Assert.Equal("unsafe_question", refused);
    }

    [Theory]
    [InlineData("Ваш пароль?")]
    [InlineData("Раsswort bitte")]
    public void Other_scripts_and_lookalike_letters_are_never_shown_as_a_form(string text)
    {
        var question = OrderToolTestData.AudienceQuestion with { Text = text };

        Assert.Null(OrderQuestionForm.Build(Clarification(question)).Form);
    }

    [Theory]
    [InlineData("Bitte PIN eingeben")]
    [InlineData("Siehe example.org")]
    [InlineData("Ja​, zusagen")]
    [InlineData("Ja, unter 030 1234567")]
    public void An_option_that_sounds_like_a_secret_or_holds_a_link_or_number_refuses_the_form(string label)
    {
        var question = Purpose with { Options = [new QuestionOption("o1", label), new QuestionOption("o2", "Nein")] };

        var (form, refused) = OrderQuestionForm.Build(Clarification(question));

        Assert.Null(form);
        Assert.Equal("unsafe_option", refused);
    }

    [Fact]
    public void Too_long_texts_bad_ids_too_many_questions_and_an_unsafe_file_name_refuse_the_form()
    {
        Assert.Equal("unsafe_question", OrderQuestionForm.Build(Clarification(Purpose with { Text = new string('a', OrderFormText.MaxQuestionChars + 1) })).Refused);
        Assert.Equal("unsafe_option", OrderQuestionForm.Build(Clarification(Purpose with { Options = [new QuestionOption("o1", new string('b', OrderFormText.MaxOptionChars + 1))] })).Refused);
        Assert.Equal("unusable_question_id", OrderQuestionForm.Build(Clarification(Purpose with { Id = "q 2" })).Refused);
        Assert.Equal("unusable_question_id", OrderQuestionForm.Build(Clarification(Purpose with { Id = "revision" })).Refused);
        Assert.Equal("unusable_options", OrderQuestionForm.Build(Clarification(Purpose with { Options = [new QuestionOption("decide_for_me", "Ja")] })).Refused);
        Assert.Equal("unusable_options", OrderQuestionForm.Build(Clarification(Purpose with { Options = [] })).Refused);
        Assert.Equal("too_many_questions", OrderQuestionForm.Build(Clarification(Purpose, Purpose with { Id = "q3" }, Purpose with { Id = "q4" }, Purpose with { Id = "q5" })).Refused);
        Assert.Equal("duplicate_question_id", OrderQuestionForm.Build(Clarification(Purpose, Purpose)).Refused);
        Assert.Equal("unsafe_file_name", OrderQuestionForm.Build(Clarification() with { RevisionFileName = "www.example.com.docx" }).Refused);
        Assert.Equal("no_questions", OrderQuestionForm.Build(OrderToolTestData.Clarification([])).Refused);
        Assert.Equal("no_clarification", OrderQuestionForm.Build(null).Refused);
    }

    [Theory]
    [InlineData("Wie lang soll der Text ungefähr werden?")]
    [InlineData("Soll die Mail z. B. einen Dank enthalten?")]
    [InlineData("Version 2.0 oder 3.1?")]
    [InlineData("Who is the e-mail for?")]
    [InlineData("Findet das Fest am 12.07.2026 statt?")]
    [InlineData("Kurz (etwa 150 Wörter)")]
    [InlineData("Sollen die Gäste geduzt oder gesiezt werden?")]
    [InlineData("Gilt der Preis von 4.900 EUR?")]
    public void Ordinary_questions_and_options_pass(string text) =>
        Assert.True(text.IsSafeForForm(OrderFormText.MaxQuestionChars), text);

    [Fact]
    public void A_sent_form_gives_one_answer_per_question_from_the_form_with_choices_you_decide_and_empty_questions()
    {
        var clarification = Clarification(OrderToolTestData.LengthQuestion, OrderToolTestData.AudienceQuestion, Purpose) with { RevisionFileName = "entwurf.docx" };

        var reading = OrderQuestionForm.Read(Accept(("length", "short"), ("q1", "decide_for_me"), ("revision", "no")), clarification);

        Assert.Equal(OrderFormOutcome.Accepted, reading.Outcome);
        Assert.Equal(
        [
            new OrderAnswer("length", "short", null, false, AnswerOrigin.Form),
            new OrderAnswer("q1", null, null, true, AnswerOrigin.Form),
            new OrderAnswer("q2", null, null, true, AnswerOrigin.Form),
            new OrderAnswer("revision", "no", null, false, AnswerOrigin.Form),
        ], reading.Answers);
    }

    [Fact]
    public void Only_you_decide_is_a_choice_and_starts_the_order()
    {
        var reading = OrderQuestionForm.Read(Accept(("length", "decide_for_me")), Clarification());

        Assert.Equal(OrderFormOutcome.Accepted, reading.Outcome);
        Assert.All(reading.Answers, a => Assert.True(a.DecideForMe));
    }

    [Fact]
    public void A_form_sent_without_a_single_choice_counts_as_closed()
    {
        Assert.Equal(new OrderFormReading(OrderFormOutcome.Cancelled, []), OrderQuestionForm.Read(Accept(), Clarification() with { RevisionFileName = "entwurf.docx" }));
        var nulls = new ElicitResult { Action = "ACCEPT", Content = new Dictionary<string, JsonElement> { ["length"] = JsonSerializer.SerializeToElement<string?>(null), ["q1"] = JsonSerializer.SerializeToElement("  ") } };
        Assert.Equal(OrderFormOutcome.Cancelled, OrderQuestionForm.Read(nulls, Clarification()).Outcome);
    }

    [Fact]
    public void Declined_and_closed_forms_and_missing_answers_give_no_answers()
    {
        Assert.Equal(OrderFormOutcome.Declined, OrderQuestionForm.Read(new ElicitResult { Action = "decline" }, Clarification()).Outcome);
        Assert.Equal(OrderFormOutcome.Cancelled, OrderQuestionForm.Read(new ElicitResult { Action = "cancel" }, Clarification()).Outcome);
        Assert.Equal(OrderFormOutcome.Cancelled, OrderQuestionForm.Read(null, Clarification()).Outcome);
        Assert.Empty(OrderQuestionForm.Read(new ElicitResult { Action = "decline" }, Clarification()).Answers);
    }

    public static TheoryData<string, object> InvalidContent() => new()
    {
        { "q1", "o9" },
        { "unknown", "x" },
        { "length_text", "eigene Worte" },
        { "q1_text", "eigene Worte" },
        { "length", 3 },
        { "revision", "maybe" },
    };

    [Theory]
    [MemberData(nameof(InvalidContent))]
    public void Anything_that_does_not_fit_the_form_makes_it_invalid(string key, object value)
    {
        var clarification = Clarification() with { RevisionFileName = "entwurf.docx" };
        var content = new Dictionary<string, JsonElement> { ["length"] = JsonSerializer.SerializeToElement("short"), [key] = JsonSerializer.SerializeToElement(value) };

        var reading = OrderQuestionForm.Read(new ElicitResult { Action = "accept", Content = content }, clarification);

        Assert.Equal(OrderFormOutcome.Invalid, reading.Outcome);
        Assert.Empty(reading.Answers);
    }

    [Fact]
    public void A_revision_answer_without_a_file_to_improve_is_invalid()
    {
        Assert.Equal(OrderFormOutcome.Invalid, OrderQuestionForm.Read(Accept(("revision", "no")), Clarification()).Outcome);
    }

    [Fact]
    public void Form_answers_pass_the_answer_rules_of_the_order_service_and_unanswered_questions_take_the_form_origin()
    {
        var reading = OrderQuestionForm.Read(Accept(("length", "medium")), Clarification());
        var record = Record(OrderToolTestData.LengthQuestion, OrderToolTestData.AudienceQuestion);

        var normalized = OrderAnswerRules.Normalize(record, reading.Answers, MaxFreeText);

        Assert.Equal([AnswerOrigin.Form, AnswerOrigin.Form], normalized.Select(a => a.Origin));
        var defaulted = OrderAnswerRules.Normalize(record, [new OrderAnswer("length", "short", null, false, AnswerOrigin.Form)], MaxFreeText);
        Assert.Equal(new OrderAnswer("q1", null, null, true, AnswerOrigin.Form), defaulted[1]);
    }

    private static IntakeRecord Record(params ClarifyQuestion[] questions) =>
        new(IntakeRecord.CurrentSchemaVersion, IntakeRecordStatus.Ok, null, 0, null, null, [], questions, [], [], [], null, BriefMode.AppendAnswers);

    private static ElicitResult Accept(params (string Key, string Value)[] values) =>
        new() { Action = "accept", Content = values.ToDictionary(v => v.Key, v => JsonSerializer.SerializeToElement(v.Value)) };
}
