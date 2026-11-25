using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

public sealed class QuestionTemplatesTests
{
    public static TheoryData<string, LengthScale> AllTemplates()
    {
        var data = new TheoryData<string, LengthScale>();
        foreach (var language in new[] { "de", "en" })
            foreach (var scale in Enum.GetValues<LengthScale>())
                data.Add(language, scale);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllTemplates))]
    public void Every_template_keeps_the_question_rule(string language, LengthScale scale)
    {
        var question = QuestionTemplates.Length(language, scale);

        Assert.Equal(QuestionTemplates.LengthQuestionId, question.Id);
        Assert.Equal(GapTopic.Length, question.Slot);
        Assert.True(question.FromTemplate);
        Assert.True(question.AllowFreeText);
        Assert.Null(question.DefaultOptionId);
        Assert.InRange(question.Text.Length, 1, QuestionGate.MaxQuestionChars);
        Assert.False(JargonBlocklist.Contains(question.Text));
        Assert.Equal(["short", "medium", "long"], question.Options.Select(o => o.Id));
        Assert.All(question.Options, o =>
        {
            Assert.InRange(o.Label.Length, 1, QuestionGate.MaxOptionChars);
            Assert.False(JargonBlocklist.Contains(o.Label));
        });
    }

    [Theory]
    [InlineData("de", true)]
    [InlineData("DE", true)]
    [InlineData("de-AT", true)]
    [InlineData("en", true)]
    [InlineData("fr", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Supports_german_and_english(string? language, bool expected) => Assert.Equal(expected, QuestionTemplates.Supports(language));

    [Fact]
    public void Length_without_template_throws()
    {
        Assert.Throws<ArgumentException>(() => QuestionTemplates.Length("fr", LengthScale.Standard));
    }

    [Theory]
    [InlineData("de", LengthScale.ShortForm, "short", 50, "Sehr kurz (etwa 50 Wörter)")]
    [InlineData("de", LengthScale.ShortForm, "medium", 120, "Kurz (etwa 120 Wörter)")]
    [InlineData("de", LengthScale.ShortForm, "long", 250, "Etwas länger (etwa 250 Wörter)")]
    [InlineData("de", LengthScale.Standard, "short", 150, "Kurz (etwa 150 Wörter)")]
    [InlineData("de", LengthScale.Standard, "medium", 400, "Mittel (etwa 400 Wörter)")]
    [InlineData("de", LengthScale.Standard, "long", 800, "Lang (etwa 800 Wörter)")]
    [InlineData("de", LengthScale.LongForm, "short", 800, "Kompakt (etwa 800 Wörter)")]
    [InlineData("de", LengthScale.LongForm, "medium", 1_500, "Mittel (etwa 1.500 Wörter)")]
    [InlineData("de", LengthScale.LongForm, "long", 3_000, "Ausführlich (etwa 3.000 Wörter)")]
    [InlineData("en", LengthScale.ShortForm, "short", 50, "Very short (about 50 words)")]
    [InlineData("en", LengthScale.ShortForm, "medium", 120, "Short (about 120 words)")]
    [InlineData("en", LengthScale.ShortForm, "long", 250, "A bit longer (about 250 words)")]
    [InlineData("en", LengthScale.Standard, "short", 150, "Short (about 150 words)")]
    [InlineData("en", LengthScale.Standard, "medium", 400, "Medium (about 400 words)")]
    [InlineData("en", LengthScale.Standard, "long", 800, "Long (about 800 words)")]
    [InlineData("en", LengthScale.LongForm, "short", 800, "Compact (about 800 words)")]
    [InlineData("en", LengthScale.LongForm, "medium", 1_500, "Medium (about 1,500 words)")]
    [InlineData("en", LengthScale.LongForm, "long", 3_000, "Detailed (about 3,000 words)")]
    public void Every_option_becomes_an_approximate_word_count(string language, LengthScale scale, string option, int words, string label)
    {
        var constraint = QuestionTemplates.ToLengthConstraint(new OrderAnswer("length", option, null, false, AnswerOrigin.User), language, scale);

        var question = language == "de" ? QuestionTemplates.GermanLengthQuestion : QuestionTemplates.EnglishLengthQuestion;
        Assert.Equal(new LengthConstraint(LengthUnit.Words, words, words, $"{question} → {label}", LengthKind.About), constraint);
    }

    [Fact]
    public void Free_text_gives_no_constraint()
    {
        Assert.Null(QuestionTemplates.ToLengthConstraint(new OrderAnswer("length", null, "3 Minuten Rede", false, AnswerOrigin.User), "de", LengthScale.Standard));
        Assert.Null(QuestionTemplates.ToLengthConstraint(new OrderAnswer("length", "short", "aber mit Zitat", false, AnswerOrigin.User), "de", LengthScale.Standard));
    }

    [Fact]
    public void Decide_for_me_unknown_options_and_other_questions_give_no_constraint()
    {
        Assert.Null(QuestionTemplates.ToLengthConstraint(new OrderAnswer("length", "short", null, true, AnswerOrigin.User), "de", LengthScale.Standard));
        Assert.Null(QuestionTemplates.ToLengthConstraint(new OrderAnswer("length", "huge", null, false, AnswerOrigin.User), "de", LengthScale.Standard));
        Assert.Null(QuestionTemplates.ToLengthConstraint(new OrderAnswer("q1", "short", null, false, AnswerOrigin.User), "de", LengthScale.Standard));
        Assert.Null(QuestionTemplates.ToLengthConstraint(new OrderAnswer("length", "short", null, false, AnswerOrigin.User), "fr", LengthScale.Standard));
    }
}
