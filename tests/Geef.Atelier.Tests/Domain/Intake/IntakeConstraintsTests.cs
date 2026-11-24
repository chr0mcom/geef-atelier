using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

public sealed class IntakeConstraintsTests
{
    private static IntakeRecord Record(bool withLengthQuestion = true, string language = "de") => new(
        IntakeRecord.CurrentSchemaVersion, IntakeRecordStatus.Ok, "claude-sonnet-latest", 1200, null,
        QuestionGateTests.Output([], language),
        [],
        withLengthQuestion ? [QuestionTemplates.Length(language, LengthScale.Standard)] : [],
        [], [], [], null, BriefMode.AppendAnswers);

    private static readonly OrderAnswer Short = new("length", "short", null, false, AnswerOrigin.User);

    [Fact]
    public void The_answered_length_becomes_the_length_check()
    {
        var constraints = IntakeConstraints.WithAnswers(OrderConstraints.None, Record(), [Short]);

        Assert.Equal(new LengthConstraint(LengthUnit.Words, 150, 150, "Wie lang soll der Text ungefähr werden? → Kurz (etwa 150 Wörter)", LengthKind.About), constraints.Length);
        Assert.Equal(OrderConstraints.None with { Length = constraints.Length }, constraints);
    }

    [Fact]
    public void A_length_from_the_request_wins()
    {
        var fromRequest = OrderConstraints.None with { Length = new LengthConstraint(LengthUnit.Words, 200, 200, "etwa 200 Wörter", LengthKind.About) };

        Assert.Same(fromRequest, IntakeConstraints.WithAnswers(fromRequest, Record(), [Short]));
    }

    [Fact]
    public void Everything_else_stays_unchanged()
    {
        var fromRequest = OrderConstraints.None with { Language = "de", ForbidPlaceholders = true, Notes = ["n"] };

        var constraints = IntakeConstraints.WithAnswers(fromRequest, Record(), [Short]);

        Assert.Equal(fromRequest with { Length = constraints.Length }, constraints);
        Assert.NotNull(constraints.Length);
    }

    [Fact]
    public void Without_record_question_or_usable_answer_nothing_changes()
    {
        var none = OrderConstraints.None;
        Assert.Same(none, IntakeConstraints.WithAnswers(none, null, [Short]));
        Assert.Same(none, IntakeConstraints.WithAnswers(none, Record(withLengthQuestion: false), [Short]));
        Assert.Same(none, IntakeConstraints.WithAnswers(none, Record(), []));
        Assert.Same(none, IntakeConstraints.WithAnswers(none, Record(), [Short with { DecideForMe = true }]));
        Assert.Same(none, IntakeConstraints.WithAnswers(none, Record(), [new OrderAnswer("length", null, "3 Minuten Rede", false, AnswerOrigin.User)]));
        Assert.Same(none, IntakeConstraints.WithAnswers(none, Record() with { Output = null }, [Short]));
    }

    [Fact]
    public void A_client_answer_counts_like_a_person_answer()
    {
        var constraints = IntakeConstraints.WithAnswers(OrderConstraints.None, Record(language: "en"), [Short with { Origin = AnswerOrigin.Client }]);

        Assert.Equal(150, constraints.Length?.Min);
        Assert.Equal("About how long should the text be? → Short (about 150 words)", constraints.Length?.Quote);
    }
}
