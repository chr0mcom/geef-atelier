using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Microsoft.Extensions.Options;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary>
/// <see cref="IntakeBriefComposer"/>: bit-identical without input, both modes, and the reduction from the prepared brief to the appended answers.
/// The tests of stage 2 run with <see cref="ContentAssumptionMode.Defaults"/> and a shown card, where the brief must stay bit-identical.
/// </summary>
public sealed class IntakeBriefComposerTests
{
    private const string Request = "Schreib eine Einladung zum Sommerfest.\nBitte mit Anmeldung.";
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);
    private static readonly OrderEntity TheOrder = Order("user-a", Request);

    [Fact]
    public void Without_record_answers_and_profile_the_brief_is_the_request()
    {
        foreach (var mode in Enum.GetValues<BriefMode>())
        {
            var brief = Composer(mode).Compose(new OrderBriefInput(TheOrder, null, [], null, AssumptionsShown: true));
            Assert.Equal(Request, brief.Text);
            Assert.Equal([IntakeBriefComposer.RequestSection], brief.Sections);
        }
    }

    [Fact]
    public void Without_answers_the_questions_stay_open_the_assumptions_shown_are_appended_as_defaults_and_an_empty_profile_adds_nothing()
    {
        var emptyProfile = new UserProfileEntity { Username = "user-a", Signature = " ", CreatedAt = T0, UpdatedAt = T0 };

        var brief = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), [], emptyProfile, AssumptionsShown: true));

        Assert.Equal(Request + "\n\n---\n" + OpenBlock("Wer kommt?", "Gibt es ein Motto?") + "\n" + Assumptions, brief.Text);
        Assert.Equal([IntakeBriefComposer.RequestSection, IntakeBriefComposer.OpenQuestionsSection, IntakeBriefComposer.AssumptionsSection], brief.Sections);
        Assert.Contains("the answers and corrections of the person above always take precedence", IntakeBriefComposer.AssumptionsHeading, StringComparison.Ordinal);
        Assert.Contains("never state them as facts", IntakeBriefComposer.AssumptionsHeading, StringComparison.Ordinal);
        Assert.DoesNotContain(IntakeBriefComposer.AnswersHeading, brief.Text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(BriefMode.AppendAnswers)]
    [InlineData(BriefMode.PreparedBrief)]
    public void A_record_of_a_failed_or_skipped_reading_adds_no_assumption(BriefMode mode)
    {
        var fallback = IntakeRecord.Fallback(IntakeRecordStatus.Failed, "error", 10, null, mode);

        var brief = Composer(mode).Compose(new OrderBriefInput(TheOrder, fallback, [], null, AssumptionsShown: true));

        Assert.Equal(Request, brief.Text);
        Assert.DoesNotContain(IntakeRecord.NotCheckedAssumption, brief.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Appended_answers_follow_the_fixed_format()
    {
        IReadOnlyList<OrderAnswer> answers =
        [
            new("length", "short", null, false, AnswerOrigin.User),
            new("q1", "o2", "Bitte mit Grill", false, AnswerOrigin.Client),
            new("q2", null, null, true, AnswerOrigin.User),
            new("a1", null, "Eher förmlich,\nbitte", false, AnswerOrigin.User),
            new("a2", null, "Am 12. Juli", false, AnswerOrigin.Client),
        ];

        var brief = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), answers, Profile(), AssumptionsShown: true));

        Assert.Equal(
            Request + "\n\n---\n" +
            """
            Answers the person gave before writing:
            - Wie lang soll der Text ungefähr werden? → Kurz (etwa 150 Wörter)
            - Wer kommt? → Ganzes Team – "Bitte mit Grill" (answered by the assistant)
            Corrections to assumptions:
            - Lockerer, herzlicher Ton → Eher förmlich, bitte
            - Datum als Platzhalter → Am 12. Juli (answered by the assistant)
            {{OpenQuestions}}
            - Gibt es ein Motto?

            About the person (use only where it fits the text; never invent anything beyond it):
            Name: Anna Beispiel
            Address the readers: informally
            """.ReplaceLineEndings("\n").Replace("{{OpenQuestions}}", IntakeBriefComposer.OpenQuestionsHeading, StringComparison.Ordinal),
            brief.Text);
        Assert.Equal(
            [IntakeBriefComposer.RequestSection, IntakeBriefComposer.AnswersSection, IntakeBriefComposer.CorrectionsSection,
             IntakeBriefComposer.OpenQuestionsSection, IntakeBriefComposer.ProfileSection],
            brief.Sections);
        Assert.DoesNotContain(IntakeBriefComposer.AssumptionsHeading, brief.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("What will be written", brief.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_profile_adds_one_section_after_the_request()
    {
        var brief = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, null, [], Profile(), AssumptionsShown: true));

        Assert.Equal(Request + "\n\n---\n" + IntakeBriefComposer.ProfileHeading + "\nName: Anna Beispiel\nAddress the readers: informally", brief.Text);
    }

    [Fact]
    public void Free_text_only_and_empty_answers_are_shown_honestly_and_empty_or_left_answers_stay_open()
    {
        IReadOnlyList<OrderAnswer> answers =
        [
            new("length", null, "3 Minuten Rede", false, AnswerOrigin.User),
            new("q1", null, null, false, AnswerOrigin.User),
            new("q2", null, "Sommer", true, AnswerOrigin.Client),
        ];

        var text = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), answers, null, AssumptionsShown: true)).Text;

        Assert.Contains("- Wie lang soll der Text ungefähr werden? → \"3 Minuten Rede\"\n", text, StringComparison.Ordinal);
        Assert.Contains(OpenBlock("Wer kommt?", "Gibt es ein Motto?") + "\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain(IntakeBriefComposer.LeftToYou, text, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Sommer\"", text, StringComparison.Ordinal);
        Assert.EndsWith(Assumptions, text, StringComparison.Ordinal);
    }

    [Fact]
    public void The_prepared_brief_is_the_appended_brief_plus_exactly_two_sections()
    {
        IReadOnlyList<OrderAnswer> answers =
        [
            new("length", "medium", null, false, AnswerOrigin.User),
            new("q1", null, null, true, AnswerOrigin.User),
            new("q2", null, null, true, AnswerOrigin.User),
            new("a1", null, "Förmlich", false, AnswerOrigin.User),
        ];
        var input = new OrderBriefInput(TheOrder, Record(), answers, Profile(), AssumptionsShown: true);

        var appended = Composer(BriefMode.AppendAnswers).Compose(input);
        var prepared = Composer(BriefMode.PreparedBrief).Compose(input);

        Assert.Equal(
            [IntakeBriefComposer.RequestSection, IntakeBriefComposer.UnderstandingSection, IntakeBriefComposer.AnswersSection,
             IntakeBriefComposer.CorrectionsSection, IntakeBriefComposer.OpenQuestionsSection, IntakeBriefComposer.AssumptionsSection,
             IntakeBriefComposer.ProfileSection],
            prepared.Sections);
        Assert.Equal(appended.Sections, prepared.Sections.Where(s => s is not IntakeBriefComposer.UnderstandingSection));

        var understanding = "What will be written: Eine Einladung zum Sommerfest des Teams.\nText type: Einladung\n";
        var assumptions = IntakeBriefComposer.AssumptionsHeading + "\n- Datum als Platzhalter\n";
        Assert.Contains("---\n" + understanding + IntakeBriefComposer.AnswersHeading, prepared.Text, StringComparison.Ordinal);
        var open = OpenBlock("Wer kommt?", "Gibt es ein Motto?") + "\n";
        Assert.Contains("- Lockerer, herzlicher Ton → Förmlich\n" + open + assumptions + "\n", prepared.Text, StringComparison.Ordinal);
        Assert.Contains("- Lockerer, herzlicher Ton → Förmlich\n" + open + assumptions + "\n", appended.Text, StringComparison.Ordinal);
        Assert.Equal(appended.Text, prepared.Text.Replace(understanding, string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Answers_from_the_form_of_the_mcp_client_are_written_and_read_back_as_answers_of_the_person()
    {
        IReadOnlyList<OrderAnswer> fromPerson =
        [
            new("length", "short", null, false, AnswerOrigin.User),
            new("q1", "o2", "Bitte mit Grill", false, AnswerOrigin.User),
            new("q2", null, null, true, AnswerOrigin.User),
        ];
        var fromForm = fromPerson.Select(a => a with { Origin = AnswerOrigin.Form }).ToList();

        var person = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), fromPerson, null, AssumptionsShown: true));
        var form = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), fromForm, null, AssumptionsShown: true));

        Assert.Equal(person.Text, form.Text);
        Assert.DoesNotContain(IntakeBriefComposer.ByAssistant, form.Text, StringComparison.Ordinal);
        var parts = BriefingSections.Parse(form.Text);
        Assert.Contains("Wer kommt? → Ganzes Team – \"Bitte mit Grill\"", parts.PersonAnswers);
        Assert.Empty(parts.AssistantAnswers);
    }

    [Fact]
    public void Answers_without_a_matching_question_and_a_revision_answer_without_a_guess_are_not_listed()
    {
        IReadOnlyList<OrderAnswer> answers = [new("q9", "o1", null, false, AnswerOrigin.User), new("revision", "no", null, false, AnswerOrigin.User)];

        Assert.Equal(Request + "\n\n---\n" + OpenBlock("Wer kommt?", "Gibt es ein Motto?") + "\n" + Assumptions,
            Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), answers, null, AssumptionsShown: true)).Text);
    }

    [Theory]
    [InlineData("no", AnswerOrigin.User, "- Improve the attached file chosen as the text to revise? → No, write something new and use the file only as material\n")]
    [InlineData("no", AnswerOrigin.Client, "- Improve the attached file chosen as the text to revise? → No, write something new and use the file only as material (answered by the assistant)\n")]
    [InlineData("yes", AnswerOrigin.Client, "- Improve the attached file chosen as the text to revise? → Yes, improve that file (answered by the assistant)\n")]
    public void The_revision_answer_is_listed_with_its_origin(string option, AnswerOrigin origin, string expected)
    {
        var record = Record() with { RevisionSourceId = Guid.NewGuid() };

        var text = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, record, [new("revision", option, null, false, origin)], null, AssumptionsShown: true)).Text;

        Assert.Contains(IntakeBriefComposer.AnswersHeading + "\n" + expected, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_points_list_a_fact_assumption_as_not_given_and_keep_the_tone_as_a_default()
    {
        var record = Record() with
        {
            Questions = [],
            Assumptions =
            [
                new IntakeAssumption("a1", GapTopic.Tone, "Sachlicher, bestimmter Ton", AssumptionConfidence.Sure),
                new IntakeAssumption("a2", GapTopic.Facts, "Fotos des Schadens liegen als Anlage bei", AssumptionConfidence.Unsure),
            ],
        };

        var brief = Composer(BriefMode.AppendAnswers, ContentAssumptionMode.OpenPoints)
            .Compose(new OrderBriefInput(TheOrder, record, [], null, AssumptionsShown: true));

        Assert.Equal(
            Request + "\n\n---\n" +
            IntakeBriefComposer.AssumptionsHeading + "\n- Sachlicher, bestimmter Ton\n" +
            IntakeBriefComposer.OpenFactsHeading + "\n- Fotos des Schadens liegen als Anlage bei",
            brief.Text);
        Assert.Equal([IntakeBriefComposer.RequestSection, IntakeBriefComposer.AssumptionsSection, IntakeBriefComposer.OpenFactsSection], brief.Sections);
        var defaults = brief.Text[brief.Text.IndexOf(IntakeBriefComposer.AssumptionsHeading, StringComparison.Ordinal)..brief.Text.IndexOf(IntakeBriefComposer.OpenFactsHeading, StringComparison.Ordinal)];
        Assert.DoesNotContain("Fotos", defaults, StringComparison.Ordinal);
        Assert.Equal(
            "Not given by the person (never state these as facts in the text; where the text needs one of them, write a placeholder in square brackets or leave it out):",
            IntakeBriefComposer.OpenFactsHeading);
    }

    public static TheoryData<GapTopic> AllTopics()
    {
        var data = new TheoryData<GapTopic>();
        foreach (var topic in Enum.GetValues<GapTopic>())
            data.Add(topic);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllTopics))]
    public void Every_topic_goes_either_to_the_defaults_or_to_the_open_facts(GapTopic topic)
    {
        var record = Record() with { Questions = [], Assumptions = [new IntakeAssumption("a1", topic, "Annahme zum Thema", AssumptionConfidence.Sure)] };

        var text = Composer(BriefMode.AppendAnswers, ContentAssumptionMode.OpenPoints)
            .Compose(new OrderBriefInput(TheOrder, record, [], null, AssumptionsShown: true)).Text;

        var expected = topic.IsContent() ? IntakeBriefComposer.OpenFactsHeading : IntakeBriefComposer.AssumptionsHeading;
        Assert.Equal(Request + "\n\n---\n" + expected + "\n- Annahme zum Thema", text);
    }

    [Fact]
    public void A_corrected_fact_assumption_appears_only_as_a_correction()
    {
        IReadOnlyList<OrderAnswer> answers = [new("a2", null, "Am 12. Juli", false, AnswerOrigin.User)];

        var text = Composer(BriefMode.AppendAnswers, ContentAssumptionMode.OpenPoints)
            .Compose(new OrderBriefInput(TheOrder, Record() with { Questions = [] }, answers, null, AssumptionsShown: true)).Text;

        Assert.Equal(
            Request + "\n\n---\n" +
            IntakeBriefComposer.CorrectionsHeading + "\n- Datum als Platzhalter → Am 12. Juli\n" +
            IntakeBriefComposer.AssumptionsHeading + "\n- Lockerer, herzlicher Ton",
            text);
        Assert.DoesNotContain(IntakeBriefComposer.OpenFactsHeadingStart, text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ContentAssumptionMode.Defaults)]
    [InlineData(ContentAssumptionMode.OpenPoints)]
    public void Assumptions_the_person_never_saw_are_never_called_shown_to_the_person(ContentAssumptionMode content)
    {
        var text = Composer(BriefMode.AppendAnswers, content).Compose(new OrderBriefInput(TheOrder, Record(), [], null)).Text;

        Assert.DoesNotContain("shown to the person", text, StringComparison.Ordinal);
        Assert.DoesNotContain(IntakeBriefComposer.AssumptionsHeadingStart, text, StringComparison.Ordinal);
        Assert.Contains(IntakeBriefComposer.AssumptionsNotShownHeading + "\n- Lockerer, herzlicher Ton", text, StringComparison.Ordinal);
        Assert.Equal(
            "Assumptions made before writing; the person has not seen them (use them only where neither the request nor the files say otherwise; never state them as facts):",
            IntakeBriefComposer.AssumptionsNotShownHeading);
        Assert.Equal(content == ContentAssumptionMode.OpenPoints, text.Contains(IntakeBriefComposer.OpenFactsHeading + "\n- Datum als Platzhalter", StringComparison.Ordinal));
    }

    [Fact]
    public void Shown_and_not_shown_differ_only_in_the_heading()
    {
        var shown = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), [], null, AssumptionsShown: true)).Text;
        var hidden = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), [], null, AssumptionsShown: false)).Text;

        Assert.Equal(shown.Replace(IntakeBriefComposer.AssumptionsHeading, IntakeBriefComposer.AssumptionsNotShownHeading, StringComparison.Ordinal), hidden);
    }

    private static string OpenBlock(params string[] questions) =>
        IntakeBriefComposer.OpenQuestionsHeading + "\n" + string.Join('\n', questions.Select(q => "- " + q));

    private static readonly string Assumptions =
        IntakeBriefComposer.AssumptionsHeading + "\n- Lockerer, herzlicher Ton\n- Datum als Platzhalter";

    private static IntakeBriefComposer Composer(BriefMode mode, ContentAssumptionMode content = ContentAssumptionMode.Defaults) =>
        new(Options.Create(new IntakeOptions { BriefMode = mode, ContentAssumptions = content }));

    private static UserProfileEntity Profile() => new()
    {
        Username = "user-a", DisplayName = "Anna Beispiel", Formality = Formality.Informal, CreatedAt = T0, UpdatedAt = T0,
    };

    internal static IntakeRecord Record() => new(
        IntakeRecord.CurrentSchemaVersion,
        IntakeRecordStatus.Ok,
        "claude-sonnet-latest",
        9_000,
        null,
        new ClarifierOutput("Einladung", "de", "Eine Einladung zum Sommerfest des Teams.", false, null, LengthScale.Standard, [], []),
        [],
        [
            new ClarifyQuestion("length", GapTopic.Length, "Wie lang soll der Text ungefähr werden?",
                [new QuestionOption("short", "Kurz (etwa 150 Wörter)"), new QuestionOption("medium", "Mittel (etwa 400 Wörter)"), new QuestionOption("long", "Lang (etwa 800 Wörter)")],
                null, true, true),
            new ClarifyQuestion("q1", GapTopic.Audience, "Wer kommt?", [new QuestionOption("o1", "Nur das Team"), new QuestionOption("o2", "Ganzes Team")], "o1", true, false),
            new ClarifyQuestion("q2", GapTopic.Other, "Gibt es ein Motto?", [new QuestionOption("o1", "Ja"), new QuestionOption("o2", "Nein")], null, false, false),
        ],
        [
            new IntakeAssumption("a1", GapTopic.Tone, "Lockerer, herzlicher Ton", AssumptionConfidence.Unsure),
            new IntakeAssumption("a2", GapTopic.Date, "Datum als Platzhalter", AssumptionConfidence.Sure),
        ],
        [],
        [],
        null,
        BriefMode.AppendAnswers);
}
