using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Orders;

/// <summary>The answers of an order as the result shows them (D-088): question word for word, the answer or "left to Atelier", and who gave it.</summary>
public sealed class OrderGivenAnswersTests
{
    private static readonly ClarifyQuestion Length = new(
        "length", GapTopic.Length, "Wie lang soll der Text ungefähr werden?",
        [new QuestionOption("short", "Kurz"), new QuestionOption("long", "Lang")], null, true, true);

    private static readonly ClarifyQuestion Decision = new(
        "q1", GapTopic.Purpose, "Nimmst du das Angebot an?",
        [new QuestionOption("o1", "Ich nehme das Angebot an"), new QuestionOption("o2", "Ich lehne ab")], null, false, false);

    private static readonly IntakeAssumption Tone = new("a1", GapTopic.Tone, "Freundlicher Ton", AssumptionConfidence.Sure);

    [Fact]
    public void Every_kind_of_answer_and_origin_is_listed_in_the_stored_order()
    {
        IReadOnlyList<OrderAnswer> answers =
        [
            new("length", "short", "höchstens zehn Zeilen", false, AnswerOrigin.User),
            new("q1", "o1", null, false, AnswerOrigin.Form),
            new("a1", null, "Eher förmlich", false, AnswerOrigin.Client),
            new("revision", "no", null, false, AnswerOrigin.User),
            new("q9", "o1", null, false, AnswerOrigin.User),
        ];

        var given = OrderGivenAnswers.From([Length, Decision], [Tone], answers);

        Assert.Equal(
        [
            new OrderGivenAnswer("length", "Wie lang soll der Text ungefähr werden?", "Kurz – höchstens zehn Zeilen", false, AnswerOrigin.User),
            new OrderGivenAnswer("q1", "Nimmst du das Angebot an?", "Ich nehme das Angebot an", false, AnswerOrigin.Form),
            new OrderGivenAnswer("a1", "Freundlicher Ton", "Eher förmlich", false, AnswerOrigin.Client),
            new OrderGivenAnswer("revision", OrderGivenAnswers.RevisionQuestion, "No, write something new and use the file only as material", false, AnswerOrigin.User),
        ], given);
    }

    [Fact]
    public void Questions_left_to_atelier_have_no_answer()
    {
        var given = OrderGivenAnswers.From([Length, Decision], [], [new("length", null, null, true, AnswerOrigin.Form), new("q1", null, null, true, AnswerOrigin.User)]);

        Assert.All(given, g => Assert.Equal((null, true), (g.Answer, g.LeftToAtelier)));
    }

    [Fact]
    public void No_answers_give_no_entries()
    {
        Assert.Empty(OrderGivenAnswers.From(null, null, null));
        Assert.Empty(OrderGivenAnswers.From([Length], [Tone], []));
    }
}
