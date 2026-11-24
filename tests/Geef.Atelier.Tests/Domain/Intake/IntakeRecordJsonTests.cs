using Geef.Atelier.Core.Domain.Intake;

namespace Geef.Atelier.Tests.Domain.Intake;

/// <summary><see cref="IntakeRecordJson"/>: camelCase round trip, tolerant reading and the fallback record.</summary>
public sealed class IntakeRecordJsonTests
{
    private static readonly Guid RevisionSource = Guid.Parse("6a1f0c9e-2b44-4d0e-9a51-0e3c1d7f2a10");

    [Fact]
    public void A_full_record_survives_the_round_trip()
    {
        var record = FullRecord();

        var json = IntakeRecordJson.Serialize(record);
        var parsed = IntakeRecordJson.TryParse(json);

        Assert.NotNull(parsed);
        Assert.Equal(json, IntakeRecordJson.Serialize(parsed));
        Assert.Equal(record.Status, parsed.Status);
        Assert.Equal(record.Output!.Understanding, parsed.Output!.Understanding);
        Assert.Equal(GapTopic.Length, parsed.Output.Gaps[0].Topic);
        Assert.Equal(["kurz", "lang"], parsed.Output.Gaps[1].Question!.Options);
        Assert.Equal("length", parsed.Questions[0].Id);
        Assert.Equal(["short", "medium", "long"], parsed.Questions[0].Options.Select(o => o.Id));
        Assert.Equal(record.Assumptions, parsed.Assumptions);
        Assert.Equal(record.FromMemory, parsed.FromMemory);
        Assert.Equal(record.Trace, parsed.Trace);
        Assert.Equal(RevisionSource, parsed.RevisionSourceId);
        Assert.Equal(BriefMode.PreparedBrief, parsed.BriefMode);
    }

    [Fact]
    public void The_stored_form_uses_camel_case_keys_and_enum_names()
    {
        var json = IntakeRecordJson.Serialize(FullRecord());

        Assert.Contains("\"schemaVersion\":1", json, StringComparison.Ordinal);
        Assert.Contains("\"status\":\"Ok\"", json, StringComparison.Ordinal);
        Assert.Contains("\"allowFreeText\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"topic\":\"Length\"", json, StringComparison.Ordinal);
        Assert.Contains("\"briefMode\":\"PreparedBrief\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Status\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[1,2]")]
    [InlineData("{\"status\":\"Unheard\"}")]
    public void Unreadable_records_give_null(string? json) =>
        Assert.Null(IntakeRecordJson.TryParse(json));

    [Fact]
    public void Missing_lists_become_empty()
    {
        var parsed = IntakeRecordJson.TryParse("""{"schemaVersion":1,"status":"Failed","durationMs":12,"output":{"textType":"Brief","language":"de","understanding":"Ein Brief."},"questions":[{"id":"q1","slot":"Audience","text":"Wer liest?"}]}""");

        Assert.NotNull(parsed);
        Assert.Empty(parsed.Trace);
        Assert.Empty(parsed.Assumptions);
        Assert.Empty(parsed.FromMemory);
        Assert.Empty(parsed.ResearchTopics);
        Assert.Empty(parsed.Output!.Gaps);
        Assert.Empty(parsed.Output.ResearchTopics);
        Assert.Empty(parsed.Questions[0].Options);
        Assert.Equal(BriefMode.AppendAnswers, parsed.BriefMode);
    }

    [Fact]
    public void Answers_survive_the_round_trip_and_bad_input_gives_an_empty_list()
    {
        IReadOnlyList<OrderAnswer> answers =
        [
            new("length", "short", null, false, AnswerOrigin.User),
            new("q1", null, "Für das Team", false, AnswerOrigin.Client),
            new("q2", null, null, true, AnswerOrigin.User),
            new("a1", null, "Lieber förmlich", false, AnswerOrigin.User, RememberChoice.No),
        ];

        var json = IntakeRecordJson.SerializeAnswers(answers);

        Assert.Equal(answers, IntakeRecordJson.ParseAnswers(json));
        Assert.Contains("\"questionId\":\"length\"", json, StringComparison.Ordinal);
        Assert.Contains("\"origin\":\"Client\"", json, StringComparison.Ordinal);
        Assert.Empty(IntakeRecordJson.ParseAnswers(null));
        Assert.Empty(IntakeRecordJson.ParseAnswers(" "));
        Assert.Empty(IntakeRecordJson.ParseAnswers("{\"questionId\":\"q1\"}"));
        Assert.Empty(IntakeRecordJson.ParseAnswers("[{\"questionId\":\"q1\",\"origin\":\"Robot\"}]"));
        Assert.Single(IntakeRecordJson.ParseAnswers("[null,{\"questionId\":\"q1\"},{\"questionId\":\"  \"}]"));
    }

    [Fact]
    public void The_fallback_record_has_no_questions_and_one_assumption()
    {
        var record = IntakeRecord.Fallback(IntakeRecordStatus.TimedOut, "timeout", 40_000, "claude-sonnet-latest", BriefMode.AppendAnswers);

        Assert.Equal(IntakeRecord.CurrentSchemaVersion, record.SchemaVersion);
        Assert.Equal(IntakeRecordStatus.TimedOut, record.Status);
        Assert.Equal("timeout", record.ErrorCode);
        Assert.Equal(40_000, record.DurationMs);
        Assert.Equal("claude-sonnet-latest", record.Model);
        Assert.Null(record.Output);
        Assert.Empty(record.Questions);
        Assert.Empty(record.Trace);
        Assert.Empty(record.ResearchTopics);
        Assert.Null(record.RevisionSourceId);
        var assumption = Assert.Single(record.Assumptions);
        Assert.Equal(("a1", IntakeRecord.NotCheckedAssumption, AssumptionConfidence.Unsure), (assumption.Id, assumption.Text, assumption.Confidence));
        Assert.Equal(0, IntakeRecord.Fallback(IntakeRecordStatus.Failed, null, -5, null, BriefMode.AppendAnswers).DurationMs);
        Assert.NotNull(IntakeRecordJson.TryParse(IntakeRecordJson.Serialize(record)));
    }

    private static IntakeRecord FullRecord() => new(
        IntakeRecord.CurrentSchemaVersion,
        IntakeRecordStatus.Ok,
        "claude-sonnet-latest",
        8_200,
        null,
        new ClarifierOutput(
            "Einladung",
            "de",
            "Eine Einladung zum Sommerfest des Teams.",
            false,
            null,
            LengthScale.Standard,
            [
                new IntakeGap(GapTopic.Length, GapResolution.Ask, true, false, GapImpact.High, "Etwa 150 Wörter", AssumptionConfidence.Unsure, null),
                new IntakeGap(GapTopic.Tone, GapResolution.Ask, true, false, GapImpact.High, "Locker", AssumptionConfidence.Unsure,
                    new ProposedQuestion("Wie lang?", ["kurz", "lang"], 0, true)),
            ],
            ["Sommerfest Ideen"]),
        [new GateTrace(0, GapTopic.Length, GateDecision.Asked), new GateTrace(1, GapTopic.Tone, GateDecision.DroppedJargon)],
        [
            new ClarifyQuestion("length", GapTopic.Length, "Wie lang soll der Text ungefähr werden?",
                [new QuestionOption("short", "Kurz (etwa 150 Wörter)"), new QuestionOption("medium", "Mittel (etwa 400 Wörter)"), new QuestionOption("long", "Lang (etwa 800 Wörter)")],
                null, true, true),
        ],
        [new IntakeAssumption("a1", GapTopic.Tone, "Locker und herzlich", AssumptionConfidence.Unsure), new IntakeAssumption("a2", GapTopic.Date, "Datum als Platzhalter", AssumptionConfidence.Sure)],
        [new MemoryAnswer(GapTopic.Sender, "Anna Beispiel", "profile")],
        ["Sommerfest Ideen"],
        RevisionSource,
        BriefMode.PreparedBrief);
}
