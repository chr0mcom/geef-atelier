using System.Text.Json;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Mcp.Models;

namespace Geef.Atelier.Tests.Mcp.Orders;

/// <summary>
/// <c>get_order_result</c> carries the questions the person left open before writing as <c>unansweredQuestions</c>, appended after every
/// older field, with a note that tells the client to show them and not to answer them; for a changed version it names the version.
/// </summary>
public sealed class UnansweredQuestionsDtoTests
{
    private const string Question = "Darf dein Kind an der Klassenfahrt teilnehmen?";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static OrderResult Result(IReadOnlyList<string>? unanswered, int? assumptionsVersion = null, IReadOnlyList<IntakeAssumption>? assumptions = null) =>
        OrderToolTestData.Result(Guid.NewGuid(), OrderDisplayState.Delivered, "Liebe Frau Beispiel, vielen Dank für den Elternbrief.", assumptions: assumptions) with
        {
            UnansweredQuestions = unanswered,
            AssumptionsVersion = assumptionsVersion,
        };

    [Fact]
    public void The_questions_and_the_note_reach_the_client()
    {
        var dto = Result([Question]).ToDto();

        Assert.Equal([Question], dto.UnansweredQuestions!);
        Assert.Contains(OrderDtoMappings.UnansweredQuestionsNote, dto.Note, StringComparison.Ordinal);
        Assert.Null(dto.AssumptionsVersion);
        Assert.DoesNotContain("They were asked before version", dto.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void For_a_changed_version_the_version_is_named_also_without_assumptions()
    {
        var dto = Result([Question], assumptionsVersion: 1, assumptions: []).ToDto();

        Assert.Equal(1, dto.AssumptionsVersion);
        Assert.Contains("They were asked before version 1; a change may have settled them.", dto.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_questions_left_open_the_field_is_null_and_the_note_is_silent()
    {
        var dto = Result(null, assumptionsVersion: 1, assumptions: []).ToDto();

        Assert.Null(dto.UnansweredQuestions);
        Assert.Null(dto.AssumptionsVersion);
        Assert.DoesNotContain("unansweredQuestions", dto.Note ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public void The_field_is_appended_after_every_older_field()
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(Result([Question]).ToDto(), Web));

        Assert.Equal(["lengthNotice", "unansweredQuestions", "fromAboutMe", "aboutMeMissing", "givenAnswers"], doc.RootElement.EnumerateObject().Select(p => p.Name).SkipWhile(n => n != "lengthNotice"));
        Assert.Equal(Question, doc.RootElement.GetProperty("unansweredQuestions")[0].GetString());
    }

    [Fact]
    public void Questions_shown_to_the_client_never_carry_a_preselected_answer_from_the_gate()
    {
        var output = new ClarifierOutput("E-Mail", "de", "Eine Antwort an die Schule.", false, null, LengthScale.ShortForm,
            [new IntakeGap(GapTopic.Purpose, GapResolution.Ask, true, false, GapImpact.High, "Das Kind fährt mit.", AssumptionConfidence.Unsure,
                new ProposedQuestion(Question, ["Ja, es fährt mit", "Nein, es fährt nicht mit"], 0, false))],
            []);
        var gate = QuestionGate.Apply(new QuestionGateInput(output, "Antworte der Schule auf den Elternbrief.", false, new Dictionary<GapTopic, MemoryAnswer>(), 3));

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(gate.Questions.Select(q => q.ToDto()).ToList(), Web));

        Assert.Equal(JsonValueKind.Null, doc.RootElement[0].GetProperty("defaultOptionId").ValueKind);
    }

    [Fact]
    public void A_stored_question_with_a_preselected_answer_from_before_goes_out_without_it()
    {
        var stored = new ClarifyQuestion("q1", GapTopic.Purpose, Question, [new QuestionOption("o1", "Ja"), new QuestionOption("o2", "Nein")], "o1", false, false);

        Assert.Null(stored.ToDto().DefaultOptionId);
    }
}
