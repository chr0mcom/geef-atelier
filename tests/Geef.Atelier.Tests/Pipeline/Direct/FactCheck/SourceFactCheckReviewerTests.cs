using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Infrastructure.Pipeline;
using Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;
using Geef.Sdk.Context;
using Geef.Sdk.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Pipeline.Direct.FactCheck;

/// <summary>
/// <see cref="SourceFactCheckReviewer"/> against a scripted <see cref="IFactChecker"/>: the mode decides call, decision and severity;
/// the filter of the code (not the model) decides what stays and what blocks; every changed wording is checked in full, the
/// same wording takes over its round; one fallback model, never the writer's, and a model that hung is not called again in the run.
/// </summary>
public sealed class SourceFactCheckReviewerTests
{
    private const string Request = "Schreibe einen Elternbrief zum Elternabend, ohne Emojis.";
    private const string Notes = "Elternabend am 14. März im Raum 12. Material: Schere 22 €, Papier 31 €, Farbe 19 €.";
    private const string Unbacked = "Bitte geben Sie uns in der Gruppe Bescheid.";
    private const string Draft = "Liebe Eltern, der Elternabend ist am 14. März im Raum 12. " + Unbacked + " Viele Grüße";

    private static readonly ReviewerProfile Profile = SystemCrew.SourceFactCheckProfile;

    private static FactMaterial Material(FactCheckScope scope = FactCheckScope.Full, string? notes = null) =>
        new(Request, [], ["Use a friendly tone."], null, [new FactSource("S1", "notizen.txt", notes ?? Notes)], scope, null);

    private static ProposedFactFinding NotBacked(string quote = Unbacked) =>
        new(FactFindingKind.Unsupported, quote, "The material names no way to reply.", null, null, null);

    private static ProposedFactFinding AgainstInstruction(string instruction) =>
        new(FactFindingKind.Instruction, "Liebe Eltern, der Elternabend ist am 14. März", "Breaks the instruction.", instruction, null, null);

    private static ProposedFactFinding Conflict() =>
        new(FactFindingKind.SourceConflict, null, "The total does not add up.", null, "Schere 22 €, Papier 31 €", "Farbe 19 €");

    private static IRunContext Context(FactCheckMode? mode, int iteration = 1, string draft = Draft, FactMaterial? material = null)
    {
        IRunContext context = new RunContext()
            .Set(GeefKeys.CurrentIteration, iteration)
            .Set(AtelierContextKeys.CurrentDraft, draft)
            .Set(AtelierContextKeys.FactMaterial, material ?? Material());
        return mode is { } m ? context.Set(AtelierContextKeys.FactCheckMode, m) : context;
    }

    private static (SourceFactCheckReviewer Reviewer, ScriptedFactChecker Checker, FactCheckLedger Ledger) Create(
        DirectOptions? options = null, string writerModel = "claude-opus-latest", ILogger? logger = null, Guid? runId = null)
    {
        var checker = new ScriptedFactChecker();
        var ledger = new FactCheckLedger();
        var reviewer = new SourceFactCheckReviewer(Profile, checker, ledger,
            failed => FactCheckModelFallback.For(failed, ModelMatrix.ClaudeCli, writerModel),
            options ?? new DirectOptions(), runId ?? Guid.NewGuid(), logger ?? NullLogger.Instance);
        return (reviewer, checker, ledger);
    }

    [Fact]
    public async Task Off_approves_without_a_call()
    {
        var (reviewer, checker, ledger) = Create();

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.Off), default);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(result.Findings);
        Assert.Empty(checker.Calls);
        Assert.Equal(FactRoundStatus.Off, Assert.Single(ledger.Rounds).Status);
    }

    [Fact]
    public async Task Without_a_mode_in_the_context_nothing_is_called()
    {
        var (reviewer, checker, _) = Create();

        var result = await reviewer.ReviewAsync(Context(null), default);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(checker.Calls);
    }

    [Fact]
    public async Task Shadow_approves_with_info_findings_marked_as_shadow()
    {
        var (reviewer, checker, _) = Create();
        checker.Checks.Enqueue(Answer(NotBacked()));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.Shadow), default);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Equal(true, finding.Metadata![SourceFactCheckReviewer.ShadowKey]);
    }

    [Fact]
    public async Task Report_approves_and_every_finding_is_a_warning()
    {
        var (reviewer, checker, _) = Create();
        checker.Checks.Enqueue(Answer(NotBacked(), Conflict()));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.Report), default);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Equal(2, result.Findings.Count);
        Assert.All(result.Findings, f => Assert.Equal(FindingSeverity.Warning, f.Severity));
        Assert.All(result.Findings, f => Assert.Equal(SourceFactCheckReviewer.FindingCategory, f.Category));
    }

    [Fact]
    public async Task On_rejects_a_blocking_finding_as_an_error_with_its_metadata()
    {
        var (reviewer, checker, _) = Create();
        checker.Checks.Enqueue(Answer(NotBacked()));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.On), default);

        Assert.Equal(ReviewDecision.Rejected, result.Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Error, finding.Severity);
        Assert.Equal("The material names no way to reply.", finding.Message);
        var metadata = finding.Metadata!;
        Assert.Equal(FactFindingMetadata.UnsupportedCode, metadata[FactFindingMetadata.Code]);
        Assert.Equal(Unbacked, metadata[FactFindingMetadata.Quote]);
        Assert.Equal(true, metadata[FactFindingMetadata.Blocking]);
        Assert.Equal("f1", metadata[FactFindingMetadata.Id]);
        Assert.Equal("On", metadata[FactFindingMetadata.Mode]);
        Assert.False(metadata.ContainsKey(SourceFactCheckReviewer.ShadowKey));
    }

    [Fact]
    public async Task An_instruction_of_the_assistant_only_is_a_hint_and_never_blocks()
    {
        var (reviewer, checker, _) = Create();
        checker.Checks.Enqueue(Answer(AgainstInstruction("Use a friendly tone.")));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.On), default);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Equal("Client", finding.Metadata![FactFindingMetadata.Origin]);
    }

    [Fact]
    public async Task An_instruction_from_the_request_blocks_and_the_origin_comes_from_where_it_stands()
    {
        var (reviewer, checker, _) = Create();
        checker.Checks.Enqueue(Answer(AgainstInstruction("ohne Emojis")));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.On), default);

        Assert.Equal(ReviewDecision.Rejected, result.Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("Stated", finding.Metadata![FactFindingMetadata.Origin]);
        Assert.Equal("ohne Emojis", finding.Metadata[FactFindingMetadata.Instruction]);
    }

    [Fact]
    public async Task An_instruction_that_stands_nowhere_in_the_material_is_dropped()
    {
        var (reviewer, checker, _) = Create();
        checker.Checks.Enqueue(Answer(AgainstInstruction("Erwähne die Fotos im Anhang.")));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.On), default);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData(FactCheckMode.On)]
    [InlineData(FactCheckMode.Report)]
    public async Task A_source_conflict_is_never_an_error(FactCheckMode mode)
    {
        var (reviewer, checker, _) = Create();
        checker.Checks.Enqueue(Answer(Conflict()));

        var result = await reviewer.ReviewAsync(Context(mode), default);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal(FactFindingMetadata.SourceConflictCode, finding.Metadata![FactFindingMetadata.Code]);
        Assert.Equal("Schere 22 €, Papier 31 €", finding.Metadata[FactFindingMetadata.Quote]);
    }

    [Fact]
    public async Task A_quote_that_is_not_in_the_text_is_dropped_by_the_code()
    {
        var (reviewer, checker, _) = Create();
        checker.Checks.Enqueue(Answer(NotBacked("Die Fotos des Schadens liegen diesem Brief bei.")));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.On), default);

        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task Material_and_text_over_the_limit_are_checked_without_the_files()
    {
        var (reviewer, checker, ledger) = Create(new DirectOptions { FactCheckMaxMaterialChars = 200 });
        var notes = Notes + " " + new string('x', 400) + " ENDE-DER-DATEI-7Q";

        await reviewer.ReviewAsync(Context(FactCheckMode.Report, material: Material(notes: notes)), default);

        var call = Assert.Single(checker.Calls);
        Assert.Equal(FactCheckScope.WithoutFiles, call.Material.Scope);
        var prompt = FactCheckPrompts.User(call.Material, Draft);
        Assert.DoesNotContain("ENDE-DER-DATEI-7Q", prompt);
        Assert.Contains(FactCheckPrompts.WithoutFilesNote, prompt);
        Assert.Equal(FactCheckScope.WithoutFiles, Assert.Single(ledger.Rounds).Scope);
    }

    [Fact]
    public async Task Material_under_the_limit_is_checked_in_full()
    {
        var (reviewer, checker, _) = Create();

        await reviewer.ReviewAsync(Context(FactCheckMode.Report), default);

        Assert.Equal(FactCheckScope.Full, Assert.Single(checker.Calls).Material.Scope);
    }

    [Fact]
    public async Task Round_two_with_the_same_wording_takes_over_round_one_without_a_call()
    {
        var (reviewer, checker, ledger) = Create();
        await reviewer.ReviewAsync(Context(FactCheckMode.On), default);

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.On, iteration: 2), default);

        Assert.Single(checker.Calls);
        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Empty(result.Findings);
        Assert.Equal((FactRoundStatus.Checked, 0, FactCheckRound.HashOf(Draft)), (ledger.For(2)!.Status, ledger.For(2)!.Calls, ledger.For(2)!.TextHash));
    }

    [Theory]
    [InlineData(FactCheckMode.Report)]
    [InlineData(FactCheckMode.On)]
    public async Task A_text_changed_for_any_reason_is_checked_in_full_again_even_without_findings_of_round_one(FactCheckMode mode)
    {
        var (reviewer, checker, ledger) = Create();
        await reviewer.ReviewAsync(Context(mode), default);
        checker.Checks.Enqueue(Answer(NotBacked("Der Eintritt kostet 5 Euro pro Person.")));
        var longer = Draft + " Der Eintritt kostet 5 Euro pro Person.";

        var result = await reviewer.ReviewAsync(Context(mode, iteration: 2, draft: longer), default);

        Assert.Equal(2, checker.Calls.Count);
        Assert.Equal((longer, 2), (checker.Calls[1].Draft, checker.Calls[1].Iteration));
        Assert.Empty(checker.Rechecks);
        Assert.Equal("Der Eintritt kostet 5 Euro pro Person.", Assert.Single(result.Findings).Metadata![FactFindingMetadata.Quote]);
        Assert.Equal((FactRoundStatus.Checked, 1, FactCheckRound.HashOf(longer)), (ledger.For(2)!.Status, ledger.For(2)!.Calls, ledger.For(2)!.TextHash));
    }

    [Fact]
    public async Task Round_two_keeps_a_quote_that_is_still_there_open_without_a_call()
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Answer(NotBacked()));
        await reviewer.ReviewAsync(Context(FactCheckMode.On), default);

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.On, iteration: 2), default);

        Assert.Single(checker.Calls);
        Assert.Empty(checker.Rechecks);
        Assert.Equal(ReviewDecision.Rejected, result.Decision);
        Assert.Equal(Unbacked, Assert.Single(result.Findings).Metadata![FactFindingMetadata.Quote]);
        Assert.Equal(0, ledger.For(2)!.Calls);
    }

    [Fact]
    public async Task A_corrected_text_is_checked_in_full_and_a_passage_that_is_gone_is_no_longer_open()
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Answer(NotBacked(), Conflict()));
        await reviewer.ReviewAsync(Context(FactCheckMode.On), default);
        checker.Checks.Enqueue(Answer(Conflict()));

        var corrected = Draft.Replace(Unbacked, "Bei Fragen melden Sie sich bei [Kontakt].", StringComparison.Ordinal);
        var result = await reviewer.ReviewAsync(Context(FactCheckMode.On, iteration: 2, draft: corrected), default);

        Assert.Equal(2, checker.Calls.Count);
        Assert.Equal(corrected, checker.Calls[1].Draft);
        Assert.Empty(checker.Rechecks);
        Assert.Equal(ReviewDecision.Approved, result.Decision);
        Assert.Equal(FactFindingMetadata.SourceConflictCode, Assert.Single(result.Findings).Metadata![FactFindingMetadata.Code]);
        Assert.Equal(1, ledger.For(2)!.Calls);
    }

    [Theory]
    [InlineData(FactCheckMode.Report)]
    [InlineData(FactCheckMode.On)]
    public async Task A_passage_that_still_stands_stays_open_with_its_blocking_although_round_two_answers_empty(FactCheckMode mode)
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Answer(NotBacked(), Conflict()));
        await reviewer.ReviewAsync(Context(mode), default);
        checker.Checks.Enqueue(Answer());
        var longer = Draft + " Der Elternabend beginnt um 19 Uhr.";

        var result = await reviewer.ReviewAsync(Context(mode, iteration: 2, draft: longer), default);

        Assert.Equal(2, checker.Calls.Count);
        Assert.Equal(mode == FactCheckMode.On ? ReviewDecision.Rejected : ReviewDecision.Approved, result.Decision);
        var round = ledger.For(2)!;
        Assert.Equal(FactRoundStatus.Checked, round.Status);
        Assert.Equal(["f1", "f2"], round.Open.Select(f => f.Id));
        var passage = Assert.Single(round.Open, f => f.Kind == FactFindingKind.Unsupported);
        Assert.Equal((Unbacked, true), (passage.TextQuote, passage.Blocking));
        Assert.Equal("Schere 22 €, Papier 31 €", Assert.Single(round.Open, f => f.Kind == FactFindingKind.SourceConflict).MaterialQuote);
        var error = mode == FactCheckMode.On ? FindingSeverity.Error : FindingSeverity.Warning;
        Assert.Equal(error, Assert.Single(result.Findings, f => (string)f.Metadata![FactFindingMetadata.Code] == FactFindingMetadata.UnsupportedCode).Severity);
        Assert.Single(result.Findings, f => (string)f.Metadata![FactFindingMetadata.Code] == FactFindingMetadata.SourceConflictCode);
    }

    [Fact]
    public async Task A_passage_flagged_again_in_round_two_is_open_once_and_a_source_conflict_found_again_once()
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Answer(NotBacked(), Conflict()));
        await reviewer.ReviewAsync(Context(FactCheckMode.On), default);
        checker.Checks.Enqueue(Answer(NotBacked("Bitte geben Sie uns in der Gruppe Bescheid"), Conflict()));
        var longer = Draft + " Der Elternabend beginnt um 19 Uhr.";

        await reviewer.ReviewAsync(Context(FactCheckMode.On, iteration: 2, draft: longer), default);

        var round = ledger.For(2)!;
        Assert.Equal(2, round.Open.Count);
        Assert.Single(round.Open, f => f.Kind == FactFindingKind.Unsupported);
        Assert.Single(round.Open, f => f.Kind == FactFindingKind.SourceConflict);
    }

    [Fact]
    public async Task A_new_passage_of_round_two_and_an_old_one_still_there_are_both_open_and_counted_without_a_limit()
    {
        var (reviewer, checker, ledger) = Create();
        var old = Enumerable.Range(1, FactFindingFilter.MaxBlocking).Select(i => NotBacked($"Satz Nummer {i} steht so nicht im Material.")).ToArray();
        var fresh = Enumerable.Range(11, FactFindingFilter.MaxBlocking).Select(i => NotBacked($"Satz Nummer {i} steht so nicht im Material.")).ToArray();
        var first = string.Join(" ", old.Select(f => f.TextQuote));
        var second = first + " " + string.Join(" ", fresh.Select(f => f.TextQuote));
        checker.Checks.Enqueue(Answer(old));
        await reviewer.ReviewAsync(Context(FactCheckMode.On, draft: first), default);
        checker.Checks.Enqueue(Answer(fresh));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.On, iteration: 2, draft: second), default);

        Assert.Equal(2 * FactFindingFilter.MaxBlocking, ledger.For(2)!.Open.Count);
        Assert.All(ledger.For(2)!.Open, f => Assert.True(f.Blocking));
        Assert.Equal(2 * FactFindingFilter.MaxBlocking, result.Findings.Count);
    }

    [Fact]
    public async Task Passages_an_earlier_check_could_not_list_stay_counted_in_a_later_round()
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Answer(NotBacked()) with { Dropped = 2 });
        await reviewer.ReviewAsync(Context(FactCheckMode.Report), default);
        checker.Checks.Enqueue(Answer() with { Dropped = 1 });
        var longer = Draft + " Der Elternabend beginnt um 19 Uhr.";

        await reviewer.ReviewAsync(Context(FactCheckMode.Report, iteration: 2, draft: longer), default);
        await reviewer.ReviewAsync(Context(FactCheckMode.Report, iteration: 3, draft: Draft), default);

        Assert.Equal((2, 2), (ledger.For(1)!.NotShown, ledger.For(1)!.CheckNotShown));
        Assert.Equal((3, 1), (ledger.For(2)!.NotShown, ledger.For(2)!.CheckNotShown));
        Assert.Equal((2, 0, 0), (ledger.For(3)!.NotShown, ledger.For(3)!.CheckNotShown, ledger.For(3)!.Calls));
    }

    [Fact]
    public async Task A_failed_check_of_a_changed_text_throws_and_keeps_no_finding_of_round_one_as_its_own()
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Answer(NotBacked()));
        await reviewer.ReviewAsync(Context(FactCheckMode.On), default);
        checker.Checks.Enqueue(Failure("unusable_json"));

        var corrected = Draft.Replace(Unbacked, "Bei Fragen melden Sie sich bei [Kontakt].", StringComparison.Ordinal);
        await Assert.ThrowsAsync<FactCheckFailedException>(() => reviewer.ReviewAsync(Context(FactCheckMode.On, iteration: 2, draft: corrected), default));

        var round = ledger.For(2)!;
        Assert.Equal(FactRoundStatus.Failed, round.Status);
        Assert.Empty(round.Open);
        Assert.Equal(FactCheckRound.HashOf(corrected), round.TextHash);
    }

    [Fact]
    public async Task After_a_hang_the_model_is_never_called_again_in_the_run_a_later_round_goes_straight_to_the_fallback()
    {
        var options = new DirectOptions();
        var (reviewer, checker, _) = Create(options);
        checker.Checks.Enqueue(Failure(LlmFactChecker.TimeoutCode));
        checker.Checks.Enqueue(Answer(NotBacked()));
        await reviewer.ReviewAsync(Context(FactCheckMode.On), default);
        checker.Checks.Enqueue(Answer());

        var corrected = Draft.Replace(Unbacked, "Bei Fragen melden Sie sich bei [Kontakt].", StringComparison.Ordinal);
        await reviewer.ReviewAsync(Context(FactCheckMode.On, iteration: 2, draft: corrected), default);

        Assert.Equal(3, checker.Calls.Count);
        Assert.Equal(Profile.Model, checker.Calls[0].Profile.Model);
        Assert.Equal(1, checker.Calls.Count(c => c.Profile.Model == Profile.Model));
        Assert.NotEqual(Profile.Provider, checker.Calls[2].Profile.Provider);
        Assert.Equal((options.FactCheckFallbackTimeout, 2), (checker.Calls[2].Timeout, checker.Calls[2].Iteration));
    }

    [Fact]
    public async Task When_both_models_hung_earlier_a_later_round_makes_no_call_and_is_not_completed()
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Failure(LlmFactChecker.TimeoutCode));
        checker.Checks.Enqueue(Failure(LlmFactChecker.TimeoutCode));
        await Assert.ThrowsAsync<FactCheckFailedException>(() => reviewer.ReviewAsync(Context(FactCheckMode.Report), default));

        var corrected = Draft.Replace(Unbacked, "Bei Fragen melden Sie sich bei [Kontakt].", StringComparison.Ordinal);
        await Assert.ThrowsAsync<FactCheckFailedException>(() => reviewer.ReviewAsync(Context(FactCheckMode.Report, iteration: 2, draft: corrected), default));

        Assert.Equal(2, checker.Calls.Count);
        Assert.Equal((FactRoundStatus.Failed, 0), (ledger.For(2)!.Status, ledger.For(2)!.Calls));
    }

    [Fact]
    public async Task Findings_the_parser_or_the_list_limit_left_out_are_counted_as_not_shown()
    {
        var (reviewer, checker, ledger) = Create();
        var many = Enumerable.Range(1, FactFindingFilter.MaxBlocking + 2)
            .Select(i => NotBacked($"Satz Nummer {i} steht so nicht im Material."))
            .ToArray();
        var draft = string.Join(" ", many.Select(f => f.TextQuote));
        checker.Checks.Enqueue(Answer(many) with { Dropped = 3 });

        await reviewer.ReviewAsync(Context(FactCheckMode.Report, draft: draft), default);

        Assert.Equal(2 + 3, Assert.Single(ledger.Rounds).NotShown);
    }

    [Fact]
    public async Task An_unavailable_model_is_tried_once_with_the_fallback_and_never_with_the_writer_model()
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Failure("backend_unavailable"));
        checker.Checks.Enqueue(Answer(NotBacked()));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.Report), default);

        Assert.Equal(2, checker.Calls.Count);
        Assert.Equal(Profile.Model, checker.Calls[0].Profile.Model);
        Assert.Equal((ModelMatrix.ClaudeCli, "claude-sonnet-latest"), (checker.Calls[1].Profile.Provider, checker.Calls[1].Profile.Model));
        Assert.Single(result.Findings);
        var round = Assert.Single(ledger.Rounds);
        Assert.True(round.UsedFallbackModel);
        Assert.Equal(2, round.Calls);
    }

    [Fact]
    public async Task When_the_fallback_fails_too_the_reviewer_throws()
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Failure("http_503"));
        checker.Checks.Enqueue(Failure("timeout"));

        await Assert.ThrowsAsync<FactCheckFailedException>(() => reviewer.ReviewAsync(Context(FactCheckMode.On), default));

        Assert.Equal(2, checker.Calls.Count);
        Assert.Equal(FactRoundStatus.Failed, Assert.Single(ledger.Rounds).Status);
    }

    [Fact]
    public async Task A_hang_is_never_repeated_with_the_same_model_but_once_with_the_fallback_within_its_own_limit()
    {
        var options = new DirectOptions();
        var (reviewer, checker, ledger) = Create(options);
        checker.Checks.Enqueue(Failure(LlmFactChecker.TimeoutCode));
        checker.Checks.Enqueue(Answer(NotBacked()));

        var result = await reviewer.ReviewAsync(Context(FactCheckMode.Report), default);

        Assert.Equal(2, checker.Calls.Count);
        Assert.Equal((Profile.Provider, Profile.Model, options.FactCheckTimeout), (checker.Calls[0].Profile.Provider, checker.Calls[0].Profile.Model, checker.Calls[0].Timeout));
        Assert.NotEqual(Profile.Provider, checker.Calls[1].Profile.Provider);
        Assert.Equal(options.FactCheckFallbackTimeout, checker.Calls[1].Timeout);
        Assert.Single(result.Findings);
        Assert.True(Assert.Single(ledger.Rounds).UsedFallbackModel);
    }

    [Fact]
    public async Task A_second_hang_ends_the_check_as_not_completed_without_a_third_call()
    {
        var (reviewer, checker, ledger) = Create();
        checker.Checks.Enqueue(Failure(LlmFactChecker.TimeoutCode));
        checker.Checks.Enqueue(Failure(LlmFactChecker.TimeoutCode));
        checker.Checks.Enqueue(Answer(NotBacked()));

        await Assert.ThrowsAsync<FactCheckFailedException>(() => reviewer.ReviewAsync(Context(FactCheckMode.On), default));

        Assert.Equal(2, checker.Calls.Count);
        var round = Assert.Single(ledger.Rounds);
        Assert.Equal(FactRoundStatus.Failed, round.Status);
        Assert.Equal(2, round.Calls);
    }

    [Fact]
    public async Task Every_hang_is_logged_once_with_model_duration_limit_run_and_round_but_without_any_content()
    {
        var logger = new RecordingLogger();
        var runId = Guid.NewGuid();
        var (reviewer, checker, _) = Create(logger: logger, runId: runId);
        checker.Checks.Enqueue(Failure(LlmFactChecker.TimeoutCode) with { DurationMs = 90_004 });
        checker.Checks.Enqueue(Failure(LlmFactChecker.TimeoutCode) with { DurationMs = 60_002 });

        await Assert.ThrowsAsync<FactCheckFailedException>(() => reviewer.ReviewAsync(Context(FactCheckMode.Report), default));

        var hangs = logger.Entries.Where(e => e.Event == SourceFactCheckReviewer.HangEvent).ToList();
        Assert.Equal(2, hangs.Count);
        Assert.All(hangs, h => Assert.Equal(LogLevel.Warning, h.Level));
        Assert.Equal((Profile.Model, 90_004, 90, false), (hangs[0].Values["Model"], hangs[0].Values["DurationMs"], hangs[0].Values["LimitSeconds"], hangs[0].Values["Fallback"]));
        Assert.Equal((60_002, 60, true), (hangs[1].Values["DurationMs"], hangs[1].Values["LimitSeconds"], hangs[1].Values["Fallback"]));
        Assert.All(hangs, h => Assert.Equal(runId, h.Values["RunId"]));
        Assert.All(hangs, h => Assert.Equal(1, h.Values["Iteration"]));
        Assert.All(hangs, h => Assert.Equal(FactCheckSchema.CheckName, h.Values["Call"]));
        foreach (var piece in new[] { "Elternabend", "Elternbrief", "Schere", "Liebe Eltern", Unbacked })
            Assert.All(hangs, h => Assert.DoesNotContain(piece, h.Message, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failure_that_is_not_a_hang_writes_no_hang_line()
    {
        var logger = new RecordingLogger();
        var (reviewer, checker, _) = Create(logger: logger);
        checker.Checks.Enqueue(Failure("backend_unavailable"));

        await reviewer.ReviewAsync(Context(FactCheckMode.Report), default);

        Assert.DoesNotContain(logger.Entries, e => e.Event == SourceFactCheckReviewer.HangEvent);
    }

    [Fact]
    public async Task An_unreadable_answer_is_not_retried_with_another_model()
    {
        var (reviewer, checker, _) = Create();
        checker.Checks.Enqueue(Failure("no_json"));

        await Assert.ThrowsAsync<FactCheckFailedException>(() => reviewer.ReviewAsync(Context(FactCheckMode.On), default));

        Assert.Single(checker.Calls);
    }

    [Fact]
    public async Task Without_the_fallback_switch_a_failure_is_final()
    {
        var (reviewer, checker, _) = Create(new DirectOptions { FactCheckFallbackModel = false });
        checker.Checks.Enqueue(Failure("backend_unavailable"));

        await Assert.ThrowsAsync<FactCheckFailedException>(() => reviewer.ReviewAsync(Context(FactCheckMode.On), default));

        Assert.Single(checker.Calls);
    }

    [Fact]
    public async Task Missing_material_is_a_failure_not_a_silent_skip()
    {
        var (reviewer, checker, _) = Create();
        var context = new RunContext()
            .Set(GeefKeys.CurrentIteration, 1)
            .Set(AtelierContextKeys.CurrentDraft, Draft)
            .Set(AtelierContextKeys.FactCheckMode, FactCheckMode.Report);

        await Assert.ThrowsAsync<FactCheckFailedException>(() => reviewer.ReviewAsync(context, default));
        Assert.Empty(checker.Calls);
    }

    [Theory]
    [InlineData(FactCheckMode.Shadow)]
    [InlineData(FactCheckMode.Report)]
    [InlineData(FactCheckMode.On)]
    public async Task Every_mode_but_off_checks_every_order_even_a_short_one_without_files(FactCheckMode mode)
    {
        var (reviewer, checker, _) = Create();
        var bare = new FactMaterial("Schreib eine kurze Notiz.", [], [], null, [], FactCheckScope.Full, null);

        await reviewer.ReviewAsync(Context(mode, draft: "Kurze Notiz.", material: bare), default);

        Assert.Single(checker.Calls);
    }

    private static FactCheckCall Answer(params ProposedFactFinding[] findings) => new(findings, "{}", 10, "gpt-astra-latest", null);

    private static FactCheckCall Failure(string code) => new(null, null, 10, "gpt-astra-latest", code);

    private sealed record CheckRequest(FactMaterial Material, string Draft, ReviewerProfile Profile, TimeSpan Timeout, int Iteration);

    private sealed record RecheckRequest(FactMaterial Material, string Draft, IReadOnlyList<FactFinding> Previous, ReviewerProfile Profile, TimeSpan Timeout);

    private sealed class ScriptedFactChecker : IFactChecker
    {
        public Queue<FactCheckCall> Checks { get; } = new();
        public Queue<RecheckCall> RecheckAnswers { get; } = new();
        public List<CheckRequest> Calls { get; } = [];
        public List<RecheckRequest> Rechecks { get; } = [];

        public Task<FactCheckCall> CheckAsync(FactMaterial material, string draft, ReviewerProfile profile, TimeSpan timeout, int iteration, CancellationToken ct)
        {
            Calls.Add(new CheckRequest(material, draft, profile, timeout, iteration));
            return Task.FromResult(Checks.TryDequeue(out var answer) ? answer with { Model = profile.Model } : new FactCheckCall([], "{}", 10, profile.Model, null));
        }

        public Task<RecheckCall> RecheckAsync(FactMaterial material, string draft, IReadOnlyList<FactFinding> previous, ReviewerProfile profile, TimeSpan timeout, CancellationToken ct)
        {
            Rechecks.Add(new RecheckRequest(material, draft, previous, profile, timeout));
            return Task.FromResult(RecheckAnswers.TryDequeue(out var answer) ? answer with { Model = profile.Model } : new RecheckCall([], "{}", 10, profile.Model, null));
        }
    }

    private sealed record LogEntry(LogLevel Level, EventId Event, string Message, IReadOnlyDictionary<string, object?> Values);

    private sealed class RecordingLogger : ILogger
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : new Dictionary<string, object?>();
            Entries.Add(new LogEntry(logLevel, eventId, formatter(state, exception), values));
        }
    }
}
