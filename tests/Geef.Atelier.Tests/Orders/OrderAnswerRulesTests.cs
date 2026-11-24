using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Orders;

/// <summary><see cref="OrderAnswerRules"/>: every error text, completion with "decide for me" and the fixed order.</summary>
public sealed class OrderAnswerRulesTests
{
    private static readonly IntakeRecord TheRecord = IntakeBriefComposerTests.Record() with { RevisionSourceId = Guid.NewGuid() };

    public static TheoryData<OrderAnswer[], string> Invalid() => new()
    {
        { [new OrderAnswer(" ", null, null, true, AnswerOrigin.User)], "Every answer needs the id of its question." },
        { [new OrderAnswer("q7", null, null, true, AnswerOrigin.User)], "There is no question \"q7\" for this order." },
        { [new OrderAnswer("q1", "o9", null, false, AnswerOrigin.User)], "\"o9\" is not an answer option of question \"q1\"." },
        { [new OrderAnswer("length", "o1", null, false, AnswerOrigin.User)], "\"o1\" is not an answer option of question \"length\"." },
        { [new OrderAnswer("q1", null, new string('x', 501), false, AnswerOrigin.User)], "The answer to \"q1\" is too long (501 characters, at most 500)." },
        { [new OrderAnswer("a1", null, new string('x', 501), false, AnswerOrigin.User)], "The answer to \"a1\" is too long (501 characters, at most 500)." },
        { [new OrderAnswer("q1", "o1", null, false, AnswerOrigin.Memory)], "The answer to \"q1\" needs the origin \"user\" or \"client\"." },
        { [new OrderAnswer("q1", "o1", null, false, (AnswerOrigin)9)], "The answer to \"q1\" needs the origin \"user\" or \"client\"." },
        { [new OrderAnswer("q1", "o1", null, false, AnswerOrigin.User), new OrderAnswer("q1 ", "o2", null, false, AnswerOrigin.User)], "The question \"q1\" was answered more than once." },
        { [new OrderAnswer("a1", null, "  ", false, AnswerOrigin.User)], "Please write the correction for \"a1\" as free text." },
        { [new OrderAnswer("revision", "maybe", null, false, AnswerOrigin.User)], "The answer to \"revision\" must be \"yes\" or \"no\"." },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_answers_are_refused_in_plain_english(OrderAnswer[] answers, string message)
    {
        var ex = Assert.Throws<OrderAnswerException>(() => OrderAnswerRules.Normalize(TheRecord, answers, 500));
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void The_revision_answer_exists_only_when_a_revision_was_guessed()
    {
        var ex = Assert.Throws<OrderAnswerException>(() =>
            OrderAnswerRules.Normalize(TheRecord with { RevisionSourceId = null }, [new OrderAnswer("revision", "no", null, false, AnswerOrigin.User)], 500));
        Assert.Equal("There is no question \"revision\" for this order.", ex.Message);
    }

    [Fact]
    public void Partial_answers_are_completed_with_decide_for_me_in_question_order()
    {
        var result = OrderAnswerRules.Normalize(TheRecord,
        [
            new OrderAnswer("revision", "no", null, false, AnswerOrigin.User),
            new OrderAnswer("a2", " ", " Am 12. Juli ", false, AnswerOrigin.User, RememberChoice.Always),
            new OrderAnswer(" q1 ", "o2", "", false, AnswerOrigin.User),
        ], 500);

        Assert.Equal(
        [
            new OrderAnswer("length", null, null, true, AnswerOrigin.User),
            new OrderAnswer("q1", "o2", null, false, AnswerOrigin.User),
            new OrderAnswer("q2", null, null, true, AnswerOrigin.User),
            new OrderAnswer("a2", null, "Am 12. Juli", false, AnswerOrigin.User),
            new OrderAnswer("revision", "no", null, false, AnswerOrigin.User),
        ], result);
    }

    [Fact]
    public void Without_answers_every_question_is_left_to_atelier_and_a_client_only_answer_keeps_its_origin()
    {
        Assert.All(OrderAnswerRules.Normalize(TheRecord, [], 500), a => Assert.True(a.DecideForMe));
        Assert.Equal(3, OrderAnswerRules.Normalize(TheRecord, null, 500).Count);

        var client = OrderAnswerRules.Normalize(TheRecord, [new OrderAnswer("length", "long", null, false, AnswerOrigin.Client)], 500);
        Assert.All(client, a => Assert.Equal(AnswerOrigin.Client, a.Origin));
        Assert.Empty(OrderAnswerRules.Normalize(null, [], 500));
    }
}
