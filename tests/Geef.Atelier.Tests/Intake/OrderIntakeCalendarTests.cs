using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Profiles;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Tests.Domain.Intake;
using Geef.Atelier.Tests.Pipeline.Direct;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Intake;

/// <summary>
/// The order intake hands the whole file texts, the About me details and the switch <see cref="IntakeOptions.DropGuessedDateOptions"/>
/// to the gate: a date in the middle of a long file (outside the excerpt of the clarifier) or in the About me details keeps the
/// options, nothing date-like anywhere in the order drops them.
/// </summary>
public sealed class OrderIntakeCalendarTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private const string Request = "Schreib einen Aushang zum Hoffest, die Termine stehen in der Notiz.";

    private readonly IntakeOrders _orders = new();
    private readonly InMemorySourceRepository _sources = new();
    private readonly ScriptedClarifier _clarifier = new();
    private readonly RecordingRunStarter _starter = new();
    private readonly ManualTime _time = new(Now);

    private OrderIntakeService Service(bool drop, MemoryFacts? facts = null) => new(
        _orders, _sources, new IntakeShadows(), new FixedMemory(facts), _clarifier, _starter, new RecordingOrderNotifier(),
        Options.Create(new IntakeOptions { DropGuessedDateOptions = drop }), NullLogger<OrderIntakeService>.Instance, _time,
        new IntakeSweepDeferrals(_time));

    private static ClarifierOutput DateQuestion() => QuestionGateTests.Output([QuestionGateTests.Ask(
        GapTopic.Date, "Wann soll das Hoffest stattfinden?", ["Samstag, 6. Juni", "Sonntag, 7. Juni"], assumption: "Termin offen")]);

    private static string LongNote()
    {
        var filler = string.Concat(Enumerable.Repeat("Bitte Tische und Bänke mitbringen. ", 200));
        return $"{filler}Mögliche Termine: Samstag, 6. Juni oder Sonntag, 7. Juni.{filler}";
    }

    [Fact]
    public async Task Dates_from_the_middle_of_a_long_file_keep_their_options()
    {
        var order = _orders.Add(Request);
        var note = LongNote();
        Assert.True(note.Length > new IntakeOptions().ExcerptHeadChars + new IntakeOptions().ExcerptTailChars);
        _sources.Attach(order.Id, "notiz.txt", SourceKind.Text, note);
        _clarifier.Enqueue(DateQuestion());

        Assert.Equal(IntakeProcessResult.AskedQuestions, await Service(drop: true).ProcessAsync(order.Id, CancellationToken.None));

        var record = IntakeRecordJson.TryParse(Assert.Single(_orders.SavedQuestions).Json);
        Assert.Equal(["Samstag, 6. Juni", "Sonntag, 7. Juni"], Assert.Single(record!.Questions).Options.Select(o => o.Label));
    }

    [Fact]
    public async Task Dates_named_nowhere_in_the_order_turn_the_question_into_its_assumption()
    {
        var order = _orders.Add(Request);
        _sources.Attach(order.Id, "notiz.txt", SourceKind.Text, "Bitte Tische und Bänke mitbringen.");
        _clarifier.Enqueue(DateQuestion());

        Assert.Equal(IntakeProcessResult.Started, await Service(drop: true).ProcessAsync(order.Id, CancellationToken.None));

        var record = Assert.Single(_starter.Requests).Record!;
        Assert.Empty(record.Questions);
        Assert.Contains(record.Trace, t => t.Decision == GateDecision.DroppedBadOptions);
        Assert.Contains(record.Assumptions, a => a.Text == "Termin offen");
    }

    [Fact]
    public async Task Without_the_switch_the_question_is_asked_as_before()
    {
        var order = _orders.Add(Request);
        _clarifier.Enqueue(DateQuestion());

        Assert.Equal(IntakeProcessResult.AskedQuestions, await Service(drop: false).ProcessAsync(order.Id, CancellationToken.None));

        var record = IntakeRecordJson.TryParse(Assert.Single(_orders.SavedQuestions).Json);
        Assert.Equal(2, Assert.Single(record!.Questions).Options.Count);
    }

    [Fact]
    public async Task A_weekday_in_the_about_me_details_keeps_the_options()
    {
        var order = _orders.Add(Request);
        _clarifier.Enqueue(DateQuestion());
        var profile = new UserProfileEntity
        {
            Username = "e2e", DisplayName = "Alex Muster", FreeText = "Sprechzeiten montags bis donnerstags.",
            CreatedAt = Now, UpdatedAt = Now,
        };

        Assert.Equal(IntakeProcessResult.AskedQuestions, await Service(drop: true, new MemoryFacts(profile)).ProcessAsync(order.Id, CancellationToken.None));

        var record = IntakeRecordJson.TryParse(Assert.Single(_orders.SavedQuestions).Json);
        Assert.Equal(2, Assert.Single(record!.Questions).Options.Count);
    }

    private enum PreviousImageDecision
    {
        Asked, Derived, FromMemory, ToResearch, Assumed, DroppedUserCannotKnow, DroppedLowImpact, DroppedRevisionRule, DroppedLengthKnown,
        DroppedLengthSure, DroppedLengthNoTemplate, DroppedTooLong, DroppedJargon, DroppedBadOptions, DroppedLanguageMismatch,
        DroppedDuplicateTopic, DroppedOverLimit, DroppedUnsafeContent,
    }

    private sealed record PreviousImageTrace(int GapIndex, GapTopic Topic, PreviousImageDecision Decision);

    [Fact]
    public async Task The_previous_image_reads_the_record_of_a_question_the_date_rule_dropped()
    {
        var order = _orders.Add(Request);
        _clarifier.Enqueue(DateQuestion());
        await Service(drop: true).ProcessAsync(order.Id, CancellationToken.None);
        var json = IntakeRecordJson.Serialize(Assert.Single(_starter.Requests).Record!);

        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
        var trace = JsonNode.Parse(json)!["trace"]!.Deserialize<List<PreviousImageTrace>>(options)!;

        Assert.Equal(PreviousImageDecision.DroppedBadOptions, Assert.Single(trace).Decision);
        Assert.Equal(Enum.GetNames<PreviousImageDecision>(), Enum.GetNames<GateDecision>());
    }
}
