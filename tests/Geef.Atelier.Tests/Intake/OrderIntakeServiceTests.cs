using Geef.Atelier.Application.Intake;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Tests.Domain.Intake;
using Geef.Atelier.Tests.Pipeline.Direct;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Intake;

public sealed class OrderIntakeServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    private readonly IntakeOrders _orders = new();
    private readonly InMemorySourceRepository _sources = new();
    private readonly IntakeShadows _shadows = new();
    private readonly ScriptedClarifier _clarifier = new();
    private readonly RecordingRunStarter _starter = new();
    private readonly RecordingOrderNotifier _notifier = new();
    private readonly ManualTime _time = new(Now);
    private IntakeOptions _options = new();
    private FixedMemory _memory = new();
    private readonly IntakeSweepDeferrals _deferrals;

    public OrderIntakeServiceTests() => _deferrals = new IntakeSweepDeferrals(_time);

    private OrderIntakeService Service() => new(
        _orders, _sources, _shadows, _memory, _clarifier, _starter, _notifier, Options.Create(_options),
        NullLogger<OrderIntakeService>.Instance, _time, _deferrals);

    private static ClarifierOutput OutputWith(params IntakeGap[] gaps) => QuestionGateTests.Output(gaps);

    [Fact]
    public async Task No_questions_start_the_run_at_once_as_clarified()
    {
        var order = _orders.Add("Schreib eine Einladung zum Sommerfest am 12. Juli für das ganze Team, etwa 150 Wörter.");
        _clarifier.Enqueue(OutputWith(QuestionGateTests.Gap(GapTopic.Tone, GapResolution.Assume, "Locker")));

        var result = await Service().ProcessAsync(order.Id, CancellationToken.None);

        Assert.Equal(IntakeProcessResult.Started, result);
        var start = Assert.Single(_starter.Requests);
        Assert.Equal((OrderState.Analyzing, IntakeOutcome.Clarified, true), (start.Expected, start.Outcome, start.RecordIsNew));
        Assert.Empty(start.Answers);
        Assert.Equal(IntakeRecordStatus.Ok, start.Record!.Status);
        Assert.Empty(start.Record.Questions);
        Assert.Equal("Locker", Assert.Single(start.Record.Assumptions).Text);
        Assert.Equal("claude-sonnet-latest", start.Record.Model);
        Assert.Equal(1234, start.Record.DurationMs);
        Assert.Equal(_options.BriefMode, start.Record.BriefMode);
        Assert.Empty(_orders.SavedQuestions);
    }

    [Fact]
    public async Task Questions_are_stored_and_the_page_is_notified()
    {
        var order = _orders.Add("Schreib eine Einladung zum Sommerfest.");
        _clarifier.Enqueue(OutputWith(QuestionGateTests.Ask(GapTopic.Length, "", [], assumption: "Etwa 150 Wörter")));

        var result = await Service().ProcessAsync(order.Id, CancellationToken.None);

        Assert.Equal(IntakeProcessResult.AskedQuestions, result);
        Assert.Empty(_starter.Requests);
        var saved = Assert.Single(_orders.SavedQuestions);
        Assert.Equal((order.Id, Now), (saved.OrderId, saved.At));
        var record = IntakeRecordJson.TryParse(saved.Json);
        Assert.NotNull(record);
        Assert.Equal(QuestionTemplates.LengthQuestionId, Assert.Single(record.Questions).Id);
        Assert.Equal([order.Id], _notifier.Notified);
    }

    [Fact]
    public async Task A_failed_clarifier_starts_the_run_with_the_request_and_outcome_failed()
    {
        var order = _orders.Add("Schreib eine Einladung.");
        _clarifier.Enqueue(new ClarifierCallResult(IntakeRecordStatus.Failed, null, "claude-sonnet-latest", 800, "http_503"));

        var result = await Service().ProcessAsync(order.Id, CancellationToken.None);

        Assert.Equal(IntakeProcessResult.StartedAfterFailure, result);
        var start = Assert.Single(_starter.Requests);
        Assert.Equal((OrderState.Analyzing, IntakeOutcome.Failed), (start.Expected, start.Outcome));
        Assert.Equal((IntakeRecordStatus.Failed, "http_503", 800), (start.Record!.Status, start.Record.ErrorCode, start.Record.DurationMs));
        Assert.Empty(start.Record.Questions);
    }

    [Theory]
    [InlineData(IntakeRecordStatus.TimedOut)]
    [InlineData(IntakeRecordStatus.Invalid)]
    public async Task Timeouts_and_invalid_answers_start_like_a_failure(IntakeRecordStatus status)
    {
        var order = _orders.Add("Schreib eine Einladung.");
        _clarifier.Enqueue(new ClarifierCallResult(status, null, null, 40_000, "x"));

        Assert.Equal(IntakeProcessResult.StartedAfterFailure, await Service().ProcessAsync(order.Id, CancellationToken.None));
        Assert.Equal(status, Assert.Single(_starter.Requests).Record!.Status);
    }

    [Fact]
    public async Task A_clarifier_that_hangs_is_cut_off_and_the_order_starts()
    {
        _options = new IntakeOptions { Timeout = TimeSpan.FromMilliseconds(50) };
        var order = _orders.Add("Schreib eine Einladung.");
        _clarifier.Delay = TimeSpan.FromSeconds(30);
        var service = new OrderIntakeService(
            _orders, _sources, _shadows, _memory, _clarifier, _starter, _notifier, Options.Create(_options),
            NullLogger<OrderIntakeService>.Instance, TimeProvider.System, new IntakeSweepDeferrals(TimeProvider.System));

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = await service.ProcessAsync(order.Id, CancellationToken.None);

        Assert.Equal(IntakeProcessResult.StartedAfterFailure, result);
        Assert.Equal(IntakeRecordStatus.TimedOut, Assert.Single(_starter.Requests).Record!.Status);
        Assert.True(watch.Elapsed < _options.Timeout + OrderIntakeService.TimeoutGrace + TimeSpan.FromSeconds(3));
    }

    [Fact]
    public async Task A_clarifier_that_throws_starts_the_order()
    {
        var order = _orders.Add("Schreib eine Einladung.");
        _clarifier.Throw = new InvalidOperationException("boom");

        Assert.Equal(IntakeProcessResult.StartedAfterFailure, await Service().ProcessAsync(order.Id, CancellationToken.None));
        Assert.Equal(IntakeRecordStatus.Failed, Assert.Single(_starter.Requests).Record!.Status);
    }

    [Fact]
    public async Task Losing_the_race_against_skip_or_stop_ends_quietly()
    {
        var order = _orders.Add("Schreib eine Einladung zum Sommerfest, etwa 150 Wörter.");
        _clarifier.Enqueue(OutputWith());
        _starter.Refuse = true;

        Assert.Equal(IntakeProcessResult.NotAnalyzing, await Service().ProcessAsync(order.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Losing_the_race_while_saving_questions_does_not_notify()
    {
        var order = _orders.Add("Schreib eine Einladung.");
        _clarifier.Enqueue(OutputWith(QuestionGateTests.Ask(GapTopic.Audience)));
        _orders.SaveQuestionsResult = false;

        Assert.Equal(IntakeProcessResult.NotAnalyzing, await Service().ProcessAsync(order.Id, CancellationToken.None));
        Assert.Empty(_notifier.Notified);
    }

    [Theory]
    [InlineData(OrderState.Ready)]
    [InlineData(OrderState.NeedsInput)]
    [InlineData(OrderState.Cancelled)]
    public async Task Orders_that_are_no_longer_analysed_are_left_alone(OrderState state)
    {
        var order = _orders.Add("Schreib eine Einladung.", state);

        Assert.Equal(IntakeProcessResult.NotAnalyzing, await Service().ProcessAsync(order.Id, CancellationToken.None));
        Assert.Empty(_clarifier.Requests);
        Assert.Empty(_starter.Requests);
    }

    [Fact]
    public async Task Unknown_orders_are_not_found()
    {
        Assert.Equal(IntakeProcessResult.NotFound, await Service().ProcessAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task A_revision_in_the_direct_mode_names_the_file_to_revise()
    {
        var order = _orders.Add("Bitte überarbeite den angehängten Brief.");
        var letter = _sources.Attach(order.Id, "brief.txt", SourceKind.Text, "Sehr geehrte Frau Muster, …");
        _clarifier.Enqueue(QuestionGateTests.Output([], isRevision: true) with { RevisionFileNumber = 1 });

        await Service().ProcessAsync(order.Id, CancellationToken.None);

        Assert.Equal(letter.Id, Assert.Single(_starter.Requests).Record!.RevisionSourceId);
        var request = Assert.Single(_clarifier.Requests);
        Assert.True(request.RevisionHint);
        Assert.Equal("brief.txt", Assert.Single(request.Files).FileName);
    }

    [Fact]
    public async Task Profile_answers_reach_the_gate()
    {
        _memory = new FixedMemory(known: new Dictionary<GapTopic, MemoryAnswer> { [GapTopic.Sender] = new(GapTopic.Sender, "Eva Muster", "profile") });
        var order = _orders.Add("Schreib eine Einladung zum Sommerfest, etwa 150 Wörter.");
        _clarifier.Enqueue(OutputWith(QuestionGateTests.Ask(GapTopic.Sender, "Wer unterschreibt?", ["Ich", "Das Team"])));

        await Service().ProcessAsync(order.Id, CancellationToken.None);

        var start = Assert.Single(_starter.Requests);
        Assert.Equal("Eva Muster", Assert.Single(start.Record!.FromMemory).Value);
        Assert.Equal(["intake-user"], _memory.Loaded);
        Assert.Null(Assert.Single(_clarifier.Requests).ProfileBlock);
    }

    [Fact]
    public async Task The_clarifier_gets_excerpts_of_the_files()
    {
        _options = new IntakeOptions { ExcerptHeadChars = 10, ExcerptTailChars = 5 };
        var order = _orders.Add("Antworte auf den Brief.");
        _sources.Attach(order.Id, "brief.txt", SourceKind.Text, "ANFANG-0123456789-MITTE-0123456789-ENDE!");
        _clarifier.Enqueue(OutputWith());

        await Service().ProcessAsync(order.Id, CancellationToken.None);

        var file = Assert.Single(Assert.Single(_clarifier.Requests).Files);
        Assert.True(file.Truncated);
        Assert.Equal($"ANFANG-012{IntakeExcerpts.Gap}ENDE!", file.Excerpt);
        Assert.Equal(1, file.Number);
    }

    [Fact]
    public async Task Shadow_records_only_the_shadow_row_and_never_touches_the_order()
    {
        var order = _orders.Add("Schreib eine Einladung.", OrderState.Ready);
        _clarifier.Enqueue(OutputWith(QuestionGateTests.Ask(GapTopic.Audience)));

        var result = await Service().ProcessShadowAsync(order.Id, CancellationToken.None);

        Assert.Equal(IntakeProcessResult.ShadowRecorded, result);
        var row = Assert.Single(_shadows.Completed);
        Assert.Equal((order.Id, IntakeShadowStatuses.Done, 1234, (int?)1), (row.OrderId, row.Status, row.DurationMs, row.QuestionCount));
        Assert.Equal("q1", Assert.Single(IntakeRecordJson.TryParse(row.RecordJson)!.Questions).Id);
        Assert.Empty(_starter.Requests);
        Assert.Empty(_orders.SavedQuestions);
        Assert.Empty(_notifier.Notified);
        Assert.Equal(order, await _orders.GetAsync(order.Id));
    }

    [Theory]
    [InlineData(IntakeRecordStatus.TimedOut, IntakeShadowStatuses.TimedOut)]
    [InlineData(IntakeRecordStatus.Failed, IntakeShadowStatuses.Failed)]
    [InlineData(IntakeRecordStatus.Invalid, IntakeShadowStatuses.Failed)]
    public async Task Shadow_failures_are_recorded_with_their_status(IntakeRecordStatus status, string expected)
    {
        var order = _orders.Add("Schreib eine Einladung.", OrderState.Ready);
        _clarifier.Enqueue(new ClarifierCallResult(status, null, null, 500, "x"));

        await Service().ProcessShadowAsync(order.Id, CancellationToken.None);

        var row = Assert.Single(_shadows.Completed);
        Assert.Equal((expected, (int?)null), (row.Status, row.QuestionCount));
        Assert.Empty(_starter.Requests);
    }

    [Fact]
    public async Task Shadow_stops_at_the_daily_cap()
    {
        _options = new IntakeOptions { ShadowDailyCap = 3 };
        _shadows.CompletedToday = 3;
        var order = _orders.Add("Schreib eine Einladung.", OrderState.Ready);

        var result = await Service().ProcessShadowAsync(order.Id, CancellationToken.None);

        Assert.Equal(IntakeProcessResult.ShadowSkipped, result);
        Assert.Equal(IntakeShadowStatuses.SkippedCap, Assert.Single(_shadows.Completed).Status);
        Assert.Empty(_clarifier.Requests);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 0, 0, 0, TimeSpan.Zero), Assert.Single(_shadows.CountQueries));
    }

    [Fact]
    public async Task When_the_profile_cannot_be_loaded_the_order_still_starts_with_the_request_as_written()
    {
        var order = _orders.Add("Schreib eine Einladung.");
        _memory = new FixedMemory { Throw = new InvalidOperationException("db down") };

        var result = await Service().ProcessAsync(order.Id, CancellationToken.None);

        Assert.Equal(IntakeProcessResult.StartedAfterFailure, result);
        var start = Assert.Single(_starter.Requests);
        Assert.Equal((OrderState.Analyzing, IntakeOutcome.Failed), (start.Expected, start.Outcome));
        Assert.Equal((IntakeRecordStatus.Failed, OrderIntakeService.PrepareFailedCode), (start.Record!.Status, start.Record.ErrorCode));
        Assert.Empty(_clarifier.Requests);
    }

    [Fact]
    public async Task The_profile_goes_to_the_clarifier_and_a_question_it_answers_is_not_asked()
    {
        var profile = new Geef.Atelier.Core.Domain.Profiles.UserProfileEntity
        {
            Username = "intake-user", FreeText = "PROFILMARKER Kiefernweg", Formality = Geef.Atelier.Core.Domain.Profiles.Formality.Formal,
            CreatedAt = Now, UpdatedAt = Now,
        };
        var memory = new ProfileMemoryResolution(new SingleProfile(profile));
        var service = new OrderIntakeService(_orders, _sources, _shadows, memory, _clarifier, _starter, _notifier, Options.Create(_options),
            NullLogger<OrderIntakeService>.Instance, _time, _deferrals);
        var order = _orders.Add("Schreib eine Einladung zum Sommerfest, etwa 150 Wörter.");
        _clarifier.Enqueue(OutputWith(QuestionGateTests.Ask(GapTopic.Formality, "Sollen die Gäste geduzt werden?", ["Ja", "Nein"])));

        var result = await service.ProcessAsync(order.Id, CancellationToken.None);

        Assert.Contains("PROFILMARKER Kiefernweg", Assert.Single(_clarifier.Requests).ProfileBlock, StringComparison.Ordinal);
        Assert.Equal(IntakeProcessResult.Started, result);
        Assert.Empty(Assert.Single(_starter.Requests).Record!.Questions);
    }

    [Fact]
    public async Task Research_topics_with_words_of_the_about_me_details_the_clarifier_read_are_left_out_and_counted()
    {
        var profile = new Geef.Atelier.Core.Domain.Profiles.UserProfileEntity
        {
            Username = "intake-user", Organization = "Kranichsteiner GmbH", CreatedAt = Now, UpdatedAt = Now,
        };
        var memory = new ProfileMemoryResolution(new SingleProfile(profile));
        var service = new OrderIntakeService(_orders, _sources, _shadows, memory, _clarifier, _starter, _notifier, Options.Create(_options),
            NullLogger<OrderIntakeService>.Instance, _time, _deferrals);
        var order = _orders.Add("Schreib einen Aushang zum neuen Mindestlohn, etwa 150 Wörter.");
        _clarifier.Enqueue(OutputWith() with { ResearchTopics = ["Kranichsteiner Mindestlohn Tarif", "Mindestlohn 2027 Höhe"] });

        await service.ProcessAsync(order.Id, CancellationToken.None);

        var record = Assert.Single(_starter.Requests).Record!;
        Assert.Equal(["Mindestlohn 2027 Höhe"], record.ResearchTopics);
        Assert.Equal(1, record.ResearchWithheld);
        Assert.Equal(1, IntakeRecordJson.TryParse(IntakeRecordJson.Serialize(record))!.ResearchWithheld);
    }

    [Theory]
    [InlineData(ResearchMode.On, true)]
    [InlineData(ResearchMode.Off, false)]
    public async Task The_record_says_whether_research_was_switched_on_for_the_person_when_the_request_was_read(ResearchMode mode, bool allowed)
    {
        var service = new OrderIntakeService(_orders, _sources, _shadows, _memory, _clarifier, _starter, _notifier, Options.Create(_options),
            NullLogger<OrderIntakeService>.Instance, _time, _deferrals, Options.Create(new ResearchOptions { Mode = mode }));
        var order = _orders.Add("Schreib einen Aushang zum neuen Mindestlohn, etwa 150 Wörter.");
        _clarifier.Enqueue(OutputWith() with { ResearchTopics = ["Mindestlohn 2027 Höhe"] });

        await service.ProcessAsync(order.Id, CancellationToken.None);

        var record = Assert.Single(_starter.Requests).Record!;
        Assert.Equal(allowed ? true : null, record.ResearchAllowed);
        Assert.Equal(allowed ? true : null, IntakeRecordJson.TryParse(IntakeRecordJson.Serialize(record))!.ResearchAllowed);
    }

    [Fact]
    public async Task An_exception_per_person_at_the_time_of_reading_decides_the_record()
    {
        var research = new ResearchOptions { Mode = ResearchMode.On, ModeOverrides = new(StringComparer.OrdinalIgnoreCase) { ["intake-user"] = ResearchMode.Off } };
        var service = new OrderIntakeService(_orders, _sources, _shadows, _memory, _clarifier, _starter, _notifier, Options.Create(_options),
            NullLogger<OrderIntakeService>.Instance, _time, _deferrals, Options.Create(research));
        var order = _orders.Add("Schreib einen Aushang zum neuen Mindestlohn, etwa 150 Wörter.");
        _clarifier.Enqueue(OutputWith() with { ResearchTopics = ["Mindestlohn 2027 Höhe"] });

        await service.ProcessAsync(order.Id, CancellationToken.None);

        Assert.Null(Assert.Single(_starter.Requests).Record!.ResearchAllowed);
    }

    [Fact]
    public async Task Without_about_me_details_the_research_topics_stay_as_the_gate_left_them()
    {
        var order = _orders.Add("Schreib einen Aushang zum neuen Mindestlohn, etwa 150 Wörter.");
        _clarifier.Enqueue(OutputWith() with { ResearchTopics = ["Kranichsteiner Mindestlohn Tarif", "Mindestlohn 2027 Höhe"] });

        await Service().ProcessAsync(order.Id, CancellationToken.None);

        var record = Assert.Single(_starter.Requests).Record!;
        Assert.Equal(["Kranichsteiner Mindestlohn Tarif", "Mindestlohn 2027 Höhe"], record.ResearchTopics);
        Assert.Null(record.ResearchWithheld);
        Assert.DoesNotContain("researchWithheld", IntakeRecordJson.Serialize(record), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stuck_order_that_keeps_failing_neither_blocks_the_younger_ones_nor_fills_the_batch()
    {
        var broken = _orders.Add("A");
        var younger = Enumerable.Range(0, OrderIntakeService.SweepBatch + 2).Select(_ => _orders.Add("B")).ToList();
        _orders.Stuck.AddRange([broken.Id, .. younger.Select(o => o.Id)]);
        _starter.ThrowFor.Add(broken.Id);

        Assert.Equal(OrderIntakeService.SweepBatch - 1, await Service().StartStuckAsync(Now, CancellationToken.None));
        Assert.Equal(OrderIntakeService.SweepBatch, _starter.Requests.Count);

        _orders.Stuck.RemoveAll(id => younger.Take(OrderIntakeService.SweepBatch - 1).Any(o => o.Id == id));
        _starter.Requests.Clear();
        Assert.Equal(3, await Service().StartStuckAsync(Now, CancellationToken.None));
        Assert.DoesNotContain(_starter.Requests, r => r.Order.Id == broken.Id);

        _time.Now = Now + IntakeSweepDeferrals.FirstDeferral + TimeSpan.FromSeconds(1);
        _starter.Requests.Clear();
        await Service().StartStuckAsync(_time.Now, CancellationToken.None);
        Assert.Contains(_starter.Requests, r => r.Order.Id == broken.Id);
    }

    [Fact]
    public async Task Hundreds_of_older_orders_that_keep_failing_never_starve_a_younger_one()
    {
        var broken = Enumerable.Range(0, 300).Select(_ => _orders.Add("A")).ToList();
        var healthy = _orders.Add("B");
        _orders.Stuck.AddRange([.. broken.Select(o => o.Id), healthy.Id]);
        foreach (var order in broken)
            _starter.ThrowFor.Add(order.Id);

        int? startedInSweep = null;
        for (var sweep = 0; sweep < 40 && startedInSweep is null; sweep++)
        {
            _time.Now = Now + TimeSpan.FromSeconds(60.5 * sweep);
            _starter.Requests.Clear();
            await Service().StartStuckAsync(_time.Now, CancellationToken.None);
            if (_starter.Requests.Any(r => r.Order.Id == healthy.Id))
                startedInSweep = sweep;
        }

        Assert.NotNull(startedInSweep);
        Assert.InRange(startedInSweep!.Value, 15, 31);
    }

    [Fact]
    public void The_pause_doubles_with_every_failure_up_to_a_day()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), IntakeSweepDeferrals.PauseAfter(1));
        Assert.Equal(TimeSpan.FromMinutes(30), IntakeSweepDeferrals.PauseAfter(2));
        Assert.Equal(TimeSpan.FromMinutes(60), IntakeSweepDeferrals.PauseAfter(3));
        Assert.Equal(TimeSpan.FromHours(16), IntakeSweepDeferrals.PauseAfter(7));
        Assert.Equal(IntakeSweepDeferrals.MaxDeferral, IntakeSweepDeferrals.PauseAfter(8));
        Assert.Equal(IntakeSweepDeferrals.MaxDeferral, IntakeSweepDeferrals.PauseAfter(500));

        var time = new ManualTime(Now);
        var deferrals = new IntakeSweepDeferrals(time);
        var id = Guid.NewGuid();
        Assert.Equal(TimeSpan.FromMinutes(15), deferrals.Defer(id));
        time.Now += TimeSpan.FromMinutes(16);
        Assert.DoesNotContain(id, deferrals.Active());
        Assert.Equal(TimeSpan.FromMinutes(30), deferrals.Defer(id));
        Assert.Contains(id, deferrals.Active());
        time.Now += TimeSpan.FromMinutes(31) + IntakeSweepDeferrals.MaxDeferral;
        Assert.DoesNotContain(id, deferrals.Active());
        Assert.Equal(TimeSpan.FromMinutes(15), deferrals.Defer(id));
    }

    [Fact]
    public async Task An_unanswered_order_that_keeps_failing_does_not_stop_the_others()
    {
        var broken = _orders.Add("A", OrderState.NeedsInput);
        var other = _orders.Add("B", OrderState.NeedsInput);
        _orders.Unanswered.AddRange([broken.Id, other.Id]);
        _starter.ThrowFor.Add(broken.Id);

        Assert.Equal(1, await Service().AutoStartUnansweredAsync(Now, CancellationToken.None));
        Assert.Contains(_starter.Requests, r => r.Order.Id == other.Id);

        _starter.Requests.Clear();
        await Service().AutoStartUnansweredAsync(Now, CancellationToken.None);
        Assert.DoesNotContain(_starter.Requests, r => r.Order.Id == broken.Id);
    }

    [Fact]
    public async Task Cancelling_the_sweep_is_passed_on()
    {
        var order = _orders.Add("A");
        _orders.Stuck.Add(order.Id);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _starter.ThrowFor.Add(order.Id);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CancellingStarterService(this).RunAsync(cts.Token));
    }

    private sealed class CancellingStarterService(OrderIntakeServiceTests test)
    {
        public Task<int> RunAsync(CancellationToken ct)
        {
            var starter = new ThrowingCancelStarter();
            var service = new OrderIntakeService(test._orders, test._sources, test._shadows, test._memory, test._clarifier, starter, test._notifier,
                Options.Create(test._options), NullLogger<OrderIntakeService>.Instance, test._time, test._deferrals);
            return service.StartStuckAsync(Now, ct);
        }
    }

    private sealed class ThrowingCancelStarter : Geef.Atelier.Application.Orders.IOrderRunStarter
    {
        public Task<Guid?> TryStartAsync(Geef.Atelier.Application.Orders.RunStartRequest request, CancellationToken ct) =>
            Task.FromException<Guid?>(new OperationCanceledException(ct));
    }

    private sealed class SingleProfile(Geef.Atelier.Core.Domain.Profiles.UserProfileEntity profile) : Geef.Atelier.Core.Persistence.Profiles.IUserProfileRepository
    {
        public Task<Geef.Atelier.Core.Domain.Profiles.UserProfileEntity?> GetAsync(string username, CancellationToken ct = default) =>
            Task.FromResult<Geef.Atelier.Core.Domain.Profiles.UserProfileEntity?>(username == profile.Username ? profile : null);
        public Task UpsertAsync(Geef.Atelier.Core.Domain.Profiles.UserProfileEntity p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Geef.Atelier.Core.Domain.Profiles.UserProfileFillResult> FillEmptyAsync(string username, Geef.Atelier.Core.Domain.Profiles.UserProfileFill fill, DateTimeOffset now, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(string username, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Stuck_orders_start_without_questions()
    {
        var waiting = _orders.Add("A");
        var exhausted = _orders.Add("B", attempts: 2);
        var gone = _orders.Add("C", OrderState.Ready);
        _orders.Stuck.AddRange([waiting.Id, exhausted.Id, gone.Id]);

        var started = await Service().StartStuckAsync(Now, CancellationToken.None);

        Assert.Equal(2, started);
        var (staleBefore, unclaimedBefore, maxAttempts) = Assert.Single(_orders.StuckQueries);
        Assert.Equal((Now - _options.ClaimLease, Now - _options.QueueTimeout, _options.MaxAttempts), (staleBefore, unclaimedBefore, maxAttempts));
        Assert.All(_starter.Requests, r =>
        {
            Assert.Equal((OrderState.Analyzing, IntakeOutcome.Failed, true), (r.Expected, r.Outcome, r.RecordIsNew));
            Assert.Equal(IntakeRecordStatus.QueueTimeout, r.Record!.Status);
        });
        Assert.Equal([OrderIntakeService.QueueTimeoutCode, OrderIntakeService.AttemptsExhaustedCode], _starter.Requests.Select(r => r.Record!.ErrorCode));
    }

    [Fact]
    public async Task Unanswered_orders_start_after_the_deadline_with_their_record()
    {
        var record = new IntakeRecord(1, IntakeRecordStatus.Ok, "m", 1, null, OutputWith(), [], [QuestionTemplates.Length("de", LengthScale.Standard)],
            [], [], [], null, BriefMode.AppendAnswers);
        var order = _orders.Add("A", OrderState.NeedsInput, clarifyJson: IntakeRecordJson.Serialize(record));
        var started = _orders.Add("B", OrderState.Ready);
        _orders.Unanswered.AddRange([order.Id, started.Id]);

        var count = await Service().AutoStartUnansweredAsync(Now, CancellationToken.None);

        Assert.Equal(1, count);
        Assert.Equal(Now - _options.NeedsInputAutoStartAfter, Assert.Single(_orders.UnansweredQueries));
        var start = Assert.Single(_starter.Requests);
        Assert.Equal((OrderState.NeedsInput, IntakeOutcome.AutoStarted, false), (start.Expected, start.Outcome, start.RecordIsNew));
        Assert.Equal("length", Assert.Single(start.Record!.Questions).Id);
        Assert.Empty(start.Answers);
    }

    [Fact]
    public async Task The_automatic_start_can_be_switched_off()
    {
        _options = new IntakeOptions { NeedsInputAutoStartAfter = TimeSpan.Zero };
        _orders.Unanswered.Add(_orders.Add("A", OrderState.NeedsInput).Id);

        Assert.Equal(0, await Service().AutoStartUnansweredAsync(Now, CancellationToken.None));
        Assert.Empty(_orders.UnansweredQueries);
    }
}
