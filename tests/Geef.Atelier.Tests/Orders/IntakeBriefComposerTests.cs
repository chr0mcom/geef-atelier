using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Profiles;
using Microsoft.Extensions.Options;
using static Geef.Atelier.Tests.Orders.OrderTestData;

namespace Geef.Atelier.Tests.Orders;

/// <summary><see cref="IntakeBriefComposer"/>: bit-identical without input, both modes, and the reduction from the prepared brief to the appended answers.</summary>
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
            var brief = Composer(mode).Compose(new OrderBriefInput(TheOrder, null, [], null));
            Assert.Equal(Request, brief.Text);
            Assert.Equal([IntakeBriefComposer.RequestSection], brief.Sections);
        }
    }

    [Fact]
    public void A_record_without_answers_and_an_empty_profile_add_nothing_when_answers_are_appended()
    {
        var emptyProfile = new UserProfileEntity { Username = "user-a", Signature = " ", CreatedAt = T0, UpdatedAt = T0 };

        var brief = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), [], emptyProfile));

        Assert.Equal(Request, brief.Text);
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

        var brief = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), answers, Profile()));

        Assert.Equal(
            Request + "\n\n---\n" +
            """
            Answers the person gave before writing:
            - Wie lang soll der Text ungefähr werden? → Kurz (etwa 150 Wörter)
            - Wer kommt? → Ganzes Team – "Bitte mit Grill" (answered by the assistant)
            - Gibt es ein Motto? → left to you
            Corrections to assumptions:
            - Lockerer, herzlicher Ton → Eher förmlich, bitte
            - Datum als Platzhalter → Am 12. Juli (answered by the assistant)

            About the person (use only where it fits the text; never invent anything beyond it):
            Name: Anna Beispiel
            Address the readers: informally
            """.ReplaceLineEndings("\n"),
            brief.Text);
        Assert.Equal(
            [IntakeBriefComposer.RequestSection, IntakeBriefComposer.AnswersSection, IntakeBriefComposer.CorrectionsSection, IntakeBriefComposer.ProfileSection],
            brief.Sections);
        Assert.DoesNotContain(IntakeBriefComposer.AssumptionsHeading, brief.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("What will be written", brief.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_profile_adds_one_section_after_the_request()
    {
        var brief = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, null, [], Profile()));

        Assert.Equal(Request + "\n\n---\n" + IntakeBriefComposer.ProfileHeading + "\nName: Anna Beispiel\nAddress the readers: informally", brief.Text);
    }

    [Fact]
    public void Free_text_only_and_empty_answers_are_shown_honestly()
    {
        IReadOnlyList<OrderAnswer> answers =
        [
            new("length", null, "3 Minuten Rede", false, AnswerOrigin.User),
            new("q1", null, null, false, AnswerOrigin.User),
            new("q2", null, "Sommer", true, AnswerOrigin.Client),
        ];

        var text = Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), answers, null)).Text;

        Assert.Contains("- Wie lang soll der Text ungefähr werden? → \"3 Minuten Rede\"\n", text, StringComparison.Ordinal);
        Assert.Contains("- Wer kommt? → left to you\n", text, StringComparison.Ordinal);
        Assert.EndsWith("- Gibt es ein Motto? → left to you – \"Sommer\"", text, StringComparison.Ordinal);
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
        var input = new OrderBriefInput(TheOrder, Record(), answers, Profile());

        var appended = Composer(BriefMode.AppendAnswers).Compose(input);
        var prepared = Composer(BriefMode.PreparedBrief).Compose(input);

        Assert.Equal(
            [IntakeBriefComposer.RequestSection, IntakeBriefComposer.UnderstandingSection, IntakeBriefComposer.AnswersSection,
             IntakeBriefComposer.CorrectionsSection, IntakeBriefComposer.AssumptionsSection, IntakeBriefComposer.ProfileSection],
            prepared.Sections);
        Assert.Equal(appended.Sections, prepared.Sections.Where(s => s is not (IntakeBriefComposer.UnderstandingSection or IntakeBriefComposer.AssumptionsSection)));

        var understanding = "What will be written: Eine Einladung zum Sommerfest des Teams.\nText type: Einladung\n";
        var assumptions = IntakeBriefComposer.AssumptionsHeading + "\n- Datum als Platzhalter\n";
        Assert.Contains("---\n" + understanding + IntakeBriefComposer.AnswersHeading, prepared.Text, StringComparison.Ordinal);
        Assert.Contains("- Lockerer, herzlicher Ton → Förmlich\n" + assumptions + "\n", prepared.Text, StringComparison.Ordinal);
        Assert.Equal(appended.Text, prepared.Text.Replace(understanding, string.Empty, StringComparison.Ordinal).Replace(assumptions, string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Answers_without_a_matching_question_and_the_revision_answer_are_not_listed()
    {
        IReadOnlyList<OrderAnswer> answers = [new("q9", "o1", null, false, AnswerOrigin.User), new("revision", "no", null, false, AnswerOrigin.User)];

        Assert.Equal(Request, Composer(BriefMode.AppendAnswers).Compose(new OrderBriefInput(TheOrder, Record(), answers, null)).Text);
    }

    private static IntakeBriefComposer Composer(BriefMode mode) =>
        new(Options.Create(new IntakeOptions { BriefMode = mode }));

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
