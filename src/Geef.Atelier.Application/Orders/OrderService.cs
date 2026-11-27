using System.Globalization;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Intake;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Research;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Intake;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Core.Persistence.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Orders;

internal sealed class OrderService(
    IOrderRepository orders,
    ISourceRepository sources,
    ICrewService crewService,
    IRunRepository runs,
    IRunWarningSink warningSink,
    IRunWarningRepository warningRepository,
    IOrderBriefComposer briefComposer,
    IOptions<OrderOptions> orderOptions,
    IOptions<SourceOptions> sourceOptions,
    ILogger<OrderService> logger,
    ISourceIntakeService intake,
    IOrderNotifier notifier,
    TimeProvider time,
    IOrderRunStarter runStarter,
    IUserProfileRepository profiles,
    IIntakeShadowRepository shadows,
    IOptions<IntakeOptions> intakeOptions,
    IOrderIntakeSignal? intakeSignal = null) : IOrderService
{
    internal const string EmptyRequestMessage = "Please describe what you need.";
    internal const string MissingSourceMessage = "One of the files was not found. Please add it again.";
    internal const string MapNotice = "Your files are long, so the writer gets a map of them and reads the parts that matter.";
    internal const string OrderNotFoundMessage = "This order was not found.";
    internal const string StillReadingMessage = "Atelier is still reading the request. Please wait a moment and try again.";
    internal const string AlreadyStartedNotice = "This order had already started.";
    internal const string StoppedNotice = "This order was stopped.";
    internal const string NeedsAnswersNote = "The order needs your answers first.";
    internal const string AnswersNotUsedNotice = "Your answers were not used.";
    internal const int CancelAttempts = 3;
    private static readonly RunStatus[] ActiveStatuses = [RunStatus.Pending, RunStatus.Running];

    public async Task<OrderSubmission> SubmitAsync(SubmitOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CreatedByUser);

        var options = orderOptions.Value;
        var srcOptions = sourceOptions.Value;
        var text = request.RequestText?.Trim() ?? string.Empty;
        if (text.Length == 0)
            throw new OrderSubmissionException(EmptyRequestMessage);
        if (text.Length > options.RequestMaxChars)
            throw new OrderSubmissionException(Invariant(
                $"Your request is too long ({text.Length:N0} characters, the limit is {options.RequestMaxChars:N0}). Please shorten it or attach the material as a file."));

        var selections = Distinct(request.Sources);
        if (selections.Count > srcOptions.MaxFilesPerOrder)
            throw new OrderSubmissionException(Invariant($"You can use at most {srcOptions.MaxFilesPerOrder} files per order."));

        var plan = await PlanSourcesAsync(selections, request.CreatedByUser, srcOptions, ct);
        if (plan.Budget.Class == SourceBudgetClass.TooLarge)
            throw new OrderSubmissionException(TooLargeMessage(plan.Budget), TooLargeWithoutPagesMessage(plan.Budget));
        var notices = new List<string>();
        if (plan.Budget.Class == SourceBudgetClass.WithMap)
            notices.Add(MapNotice);

        var now = time.GetUtcNow();
        var route = OrderIntakeRouting.Decide(options.IntakeMode, request.AllowQuestions, request.CustomCrew is not null, text.Length, intakeOptions.Value.SkipAboveChars);
        var order = new OrderEntity
        {
            Id = Guid.NewGuid(),
            CreatedByUser = request.CreatedByUser,
            RequestText = text,
            Mode = request.Mode,
            State = route == OrderIntakeRoute.Clarify ? OrderState.Analyzing : OrderState.Ready,
            Entry = request.Entry,
            CrewTemplateName = request.Mode == OrderMode.Careful ? request.CrewTemplateName : null,
            CreatedAt = now,
            UpdatedAt = now,
        };
        var links = plan.Links.Select(l => l with { OrderId = order.Id, AddedAt = now }).ToList();

        if (route == OrderIntakeRoute.Clarify)
        {
            if (request.Mode == OrderMode.Careful)
                await crewService.ResolveOrderSnapshotAsync(request.Mode, request.CrewTemplateName, null, links.Count > 0, ct);
            await CreateAsync(() => orders.CreateAsync(order, links, ct));
            intakeSignal?.Wake();
            await NotifyAsync(order.Id);
            return new OrderSubmission(order.Id, OrderStateResolver.Resolve(order.State, null).State, null, notices);
        }

        var resolved = await crewService.ResolveOrderSnapshotAsync(request.Mode, request.CrewTemplateName, request.CustomCrew, links.Count > 0, ct,
            options.CarefulOutlineFor(request.CreatedByUser) == CarefulOutlineMode.On);
        var profile = await profiles.GetAsync(request.CreatedByUser, ct);
        var run = resolved.ToFirstRun(order, briefComposer.Compose(new OrderBriefInput(order, null, [], profile)).Text, now);
        await CreateAsync(() => orders.CreateWithFirstRunAsync(order, links, run, ct));

        await warningSink.ReportAssignmentWarningsAsync(run.Id, resolved.Warnings);
        if (route == OrderIntakeRoute.StartNowWithShadow)
            await AddShadowAsync(order.Id, now, ct);
        await NotifyAsync(order.Id);

        return new OrderSubmission(order.Id, OrderDisplayState.Queued, run.Id, notices);
    }

    public async Task<SourceSelectionCheck> CheckSourcesAsync(IReadOnlyList<OrderSourceSelection> sources, string ownerUsername, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerUsername);
        try
        {
            var plan = await PlanSourcesAsync(Distinct(sources), ownerUsername, sourceOptions.Value, ct);
            return new SourceSelectionCheck(plan.Budget.TotalChars, plan.Budget.Class, null);
        }
        catch (OrderSubmissionException ex)
        {
            return new SourceSelectionCheck(0, SourceBudgetClass.TooLarge, ex.Message);
        }
    }

    public async Task<OrderView?> GetAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        var order = await GetOwnedAsync(orderId, requestingUsername, ct);
        return order is null ? null : await BuildViewAsync(order, ct);
    }

    public Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default) =>
        GetResultAsync(orderId, requestingUsername, requestingUsername, ct);

    public async Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, string? viewerUsername, CancellationToken ct = default)
    {
        var order = await GetOwnedAsync(orderId, requestingUsername, ct);
        if (order is null)
            return null;

        var viewer = viewerUsername ?? requestingUsername;
        var showProfile = viewer is not null && string.Equals(viewer, order.CreatedByUser, StringComparison.Ordinal);
        var givenAnswers = showProfile ? GivenAnswers(order) : null;
        var orderRuns = await orders.ListRunsAsync(orderId, ct);
        var current = OrderStateResolver.SelectCurrentRun(orderRuns);
        var display = OrderStateResolver.Resolve(order.State, current?.ToRunState(time.GetUtcNow()));
        if (current is null)
            return new OrderResult(orderId, display, null, null, null, null, [], [], [], [], NoteFor(display), UncorrectedAssumptions(order), GivenAnswers: givenAnswers);

        var shown = OrderStateResolver.SelectShownRun(orderRuns);
        var run = shown ?? current;
        var report = run.ReportJson is null ? null : RunReportJson.TryParse(run.ReportJson);
        var usages = await sources.ListRunUsagesAsync(run.Id, ct);
        var hasText = !string.IsNullOrWhiteSpace(run.FinalText);
        var lookedUp = string.IsNullOrWhiteSpace(order.ResearchJson)
            ? null
            : OrderResearchLines.Build(ResearchJson.TryParse(order.ResearchJson), report?.LookedUp == true, showSearchWords: showProfile, hasText: hasText);
        var warnings = (await warningRepository.ListByRunAsync(run.Id, ct)).WithoutShownResearchWarning(lookedUp);

        var result = new OrderResult(
            orderId,
            display,
            run.Id,
            run.OrderVersion,
            hasText ? run.FinalText : null,
            hasText ? run.WordCount : null,
            report?.Placeholders ?? [],
            report?.Checks.Where(c => !c.Passed).ToList() ?? [],
            usages.Where(u => !string.Equals(u.Target, DossierRenderer.UsageTarget, StringComparison.Ordinal))
                .Select(u => new OrderSourceUsageView(u.FileName, u.Mode, u.CharsIncluded, u.PagesIncluded)).ToList(),
            warnings,
            ShownNote(display, current, shown),
            UncorrectedAssumptions(order),
            NotGivenFacts: NotGivenFacts(order, run),
            LookedUp: lookedUp,
            LatestVersion: current.OrderVersion,
            AssumptionsVersion: string.IsNullOrWhiteSpace(run.RevisionHint) ? null : FirstVersion(orderRuns),
            LengthNotice: hasText && run.GrewBeyondItsBase() ? RevisionLines.LongerThanBefore : null,
            UnansweredQuestions: UnansweredQuestions(run),
            GivenAnswers: givenAnswers);
        if (showProfile && hasText)
            result = await WithAboutMeAsync(result, order, run, ct);
        return report is null || !hasText ? result : result.WithReport(report, showProfile);
    }

    private async Task<OrderResult> WithAboutMeAsync(OrderResult result, OrderEntity order, RunEntity run, CancellationToken ct)
    {
        if (orderOptions.Value.TellOnce == TellOnceMode.Off || string.IsNullOrWhiteSpace(order.CreatedByUser))
            return result;
        var record = IntakeRecordJson.TryParse(order.ClarifyJson);
        var fromAboutMe = AboutMeTopics.FromAboutMe(run.BriefingText, record);
        var answers = IntakeRecordJson.ParseAnswers(order.AnswersJson);
        var tellOnce = AboutMeTopics.Missing(record, answers, null);
        if (tellOnce.Count > 0)
            tellOnce = AboutMeTopics.Missing(record, answers, await profiles.GetAsync(order.CreatedByUser, ct));
        return result with
        {
            FromAboutMe = fromAboutMe.Count > 0 ? fromAboutMe : null,
            TellOnce = tellOnce.Count > 0 ? tellOnce : null,
        };
    }

    public async Task<IReadOnlyList<OrderListItem>> ListAsync(string? ownerUsername, int limit = 50, CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");

        var rows = await orders.ListAsync(ownerUsername, limit, ct);
        return rows.Select(r =>
        {
            var display = OrderStateResolver.Resolve(r.Order.State, r.CurrentRun?.ToRunState(time.GetUtcNow()));
            var listed = r.ShownRun ?? (display.HasText ? r.CurrentRun : null);
            return new OrderListItem(
                r.Order.Id,
                r.Order.RequestText.ToOrderTitle(),
                display,
                r.Order.Mode,
                r.Order.CreatedAt,
                r.Order.UpdatedAt,
                r.SourceCount,
                listed?.WordCount,
                (listed ?? r.CurrentRun)?.OrderVersion);
        }).ToList();
    }

    public async Task<OrderView?> WaitForChangeAsync(Guid orderId, string? requestingUsername, OrderDisplayState? knownState, TimeSpan maxWait, CancellationToken ct = default)
    {
        var options = orderOptions.Value;
        var limit = TimeSpan.FromSeconds(Math.Max(0, options.MaxWaitSeconds));
        var wait = maxWait < TimeSpan.Zero ? TimeSpan.Zero : maxWait > limit ? limit : maxWait;
        var poll = options.WaitPollInterval > TimeSpan.Zero ? options.WaitPollInterval : TimeSpan.FromSeconds(1);
        var deadline = time.GetUtcNow() + wait;

        while (true)
        {
            var view = await GetAsync(orderId, requestingUsername, ct);
            if (view is null || view.Display.IsFinished || knownState is null || view.Display.State != knownState)
                return view;

            var remaining = deadline - time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return view;
            await Task.Delay(remaining < poll ? remaining : poll, time, ct);
        }
    }

    public async Task<bool> CancelAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        var order = await GetOwnedAsync(orderId, requestingUsername, ct);
        if (order is null)
            return false;

        var changed = false;
        for (var attempt = 1; ; attempt++)
        {
            var current = OrderStateResolver.SelectCurrentRun(await orders.ListRunsAsync(orderId, ct));
            if (current is not null)
            {
                if (!ActiveStatuses.Contains(current.Status))
                    break;
                if (current.CancellationRequested)
                    return true;
                changed = await runs.RequestCancellationAsync(current.Id, ct);
                break;
            }

            if (order.State is OrderState.Cancelled)
                break;
            if (await orders.TrySetStateAsync(orderId, order.State, OrderState.Cancelled, ct))
            {
                changed = true;
                break;
            }

            if (attempt >= CancelAttempts || await orders.GetAsync(orderId, ct) is not { } reloaded)
                break;
            order = reloaded;
        }

        if (changed)
            await NotifyAsync(orderId);
        return changed;
    }

    public async Task<bool> DeleteAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        var order = await GetOwnedAsync(orderId, requestingUsername, ct);
        if (order is null)
            return false;

        var orderRuns = await orders.ListRunsAsync(orderId, ct);
        if (orderRuns.Any(r => ActiveStatuses.Contains(r.Status)))
            throw new OrderStillWorkingException();

        var linked = await sources.ListForOrderAsync(orderId, includeText: false, ct);
        if (!await orders.DeleteAsync(orderId, ct))
            return false;

        foreach (var item in linked.Where(i => !i.Source.InLibrary))
        {
            try
            {
                await intake.DeleteAsync(item.Source.Id, null, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Source {SourceId} of deleted order {OrderId} could not be deleted; the cleanup will retry.", item.Source.Id, orderId);
            }
        }

        await NotifyAsync(orderId);
        return true;
    }

    public async Task<OrderSubmission> AnswerAsync(AnswerOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var order = await GetOwnedAsync(request.OrderId, request.RequestingUsername, ct)
                    ?? throw new OrderAnswerException(OrderNotFoundMessage);
        if (order.State == OrderState.Analyzing)
            throw new OrderAnswerException(StillReadingMessage);
        var given = request.Answers is { Count: > 0 };
        if (order.State != OrderState.NeedsInput)
            return await CurrentWithNoticeAsync(order.Id, ct, given);

        var record = IntakeRecordJson.TryParse(order.ClarifyJson);
        var answers = OrderAnswerRules.Normalize(record, request.Answers, intakeOptions.Value.MaxFreeTextChars);
        var start = given
            ? new RunStartRequest(order, OrderState.NeedsInput, record, answers, IntakeOutcome.Answered, RecordIsNew: false)
            : new RunStartRequest(order, OrderState.NeedsInput, record, [], IntakeOutcome.Skipped, RecordIsNew: false);
        var runId = await runStarter.TryStartAsync(start, ct);
        return runId is null ? await CurrentWithNoticeAsync(order.Id, ct, given) : await SubmissionAsync(order.Id, [], ct);
    }

    public async Task<OrderRevision> ReviseAsync(ReviseOrderRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var options = orderOptions.Value;
        var order = await GetOwnedAsync(request.OrderId, request.RequestingUsername, ct);
        if (order is null || request.RequestingUsername is null || !string.Equals(order.CreatedByUser, request.RequestingUsername, StringComparison.Ordinal))
            throw new OrderRevisionException(RevisionLines.NotFound);
        if (options.ReviseModeFor(order.CreatedByUser) != ReviseMode.On)
            throw new OrderRevisionException(RevisionLines.NotAvailable);
        if (request.Quick?.Any(q => !q.IsOffered()) == true)
            throw new OrderRevisionException(RevisionLines.LongerNeedsDetails);

        var words = request.Change?.Trim() ?? string.Empty;
        if (words.Length > options.RevisionHintMaxChars)
            throw new OrderRevisionException(string.Format(CultureInfo.InvariantCulture, RevisionLines.TooLongFormat, words.Length, options.RevisionHintMaxChars));
        var change = RevisionBrief.Change(words, request.Quick ?? []);
        if (change.Length == 0)
            throw new OrderRevisionException(RevisionLines.NoChangeGiven);
        if (order.State == OrderState.Cancelled)
            throw new OrderRevisionException(RevisionLines.Stopped);

        var orderRuns = await orders.ListRunsAsync(order.Id, ct);
        var now = time.GetUtcNow();
        var current = OrderStateResolver.SelectCurrentRun(orderRuns);
        if (orderRuns.Any(r => ActiveStatuses.Contains(r.Status)) || current?.ToRunState(now).Finishing == true)
            return await BusyAsync(order, ct);

        var shown = OrderStateResolver.SelectShownRun(orderRuns)
                    ?? throw new OrderRevisionException(RevisionLines.NothingToChange);
        var next = orderRuns.Where(r => r.Kind == RunKind.Standard).Max(r => r.OrderVersion ?? 0) + 1;
        if (next > options.MaxVersionsPerOrder)
            throw new OrderRevisionException(TooManyVersions(options));

        var linked = await sources.ListForOrderAsync(order.Id, includeText: false, ct);
        var resolved = await crewService.ResolveOrderSnapshotAsync(OrderMode.Direct, null, null, linked.Count > 0, ct);
        var briefing = RevisionBrief.Compose(shown.BriefingText ?? string.Empty, change, next);
        var run = resolved.ToRevisionRun(order, shown, briefing, change, next, now);

        var (result, version) = await orders.TryAddRevisionRunAsync(order.Id, shown.Id, run, options.MaxVersionsPerOrder, ct);
        switch (result)
        {
            case RevisionStart.Created:
                break;
            case RevisionStart.Busy or RevisionStart.BaseNotShown:
                return await BusyAsync(order, ct);
            case RevisionStart.TooManyVersions:
                throw new OrderRevisionException(TooManyVersions(options));
            case RevisionStart.Cancelled:
                throw new OrderRevisionException(RevisionLines.Stopped);
            default:
                throw new OrderRevisionException(RevisionLines.NotFound);
        }

        await warningSink.ReportAssignmentWarningsAsync(run.Id, resolved.Warnings);
        await NotifyAsync(order.Id);
        logger.LogInformation("Order {OrderId} gets version {Version} as a change of version {BaseVersion} ({Entry}).", order.Id, version, shown.OrderVersion, request.Entry);
        return new OrderRevision(order.Id, OrderDisplayState.Queued, run.Id, version, []);
    }

    private async Task<OrderRevision> BusyAsync(OrderEntity order, CancellationToken ct)
    {
        var current = OrderStateResolver.SelectCurrentRun(await orders.ListRunsAsync(order.Id, ct));
        var display = OrderStateResolver.Resolve(order.State, current?.ToRunState(time.GetUtcNow()));
        return new OrderRevision(order.Id, display.State, current?.Id, current?.OrderVersion, [RevisionLines.Busy]);
    }

    private static string TooManyVersions(OrderOptions options) =>
        string.Format(CultureInfo.InvariantCulture, RevisionLines.TooManyVersionsFormat, options.MaxVersionsPerOrder);

    public async Task<OrderSubmission> SkipClarificationAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        var order = await GetOwnedAsync(orderId, requestingUsername, ct)
                    ?? throw new OrderAnswerException(OrderNotFoundMessage);
        if (order.State is not (OrderState.Analyzing or OrderState.NeedsInput))
            return await CurrentWithNoticeAsync(order.Id, ct);

        var record = order.State == OrderState.NeedsInput ? IntakeRecordJson.TryParse(order.ClarifyJson) : null;
        var runId = await runStarter.TryStartAsync(
            new RunStartRequest(order, order.State, record, [], IntakeOutcome.Skipped, RecordIsNew: false), ct);
        return runId is null ? await CurrentWithNoticeAsync(order.Id, ct) : await SubmissionAsync(order.Id, [], ct);
    }

    private async Task<OrderSubmission> CurrentWithNoticeAsync(Guid orderId, CancellationToken ct, bool answersGiven = false)
    {
        var order = await orders.GetAsync(orderId, ct) ?? throw new OrderAnswerException(OrderNotFoundMessage);
        var notice = order.State switch
        {
            OrderState.Cancelled => StoppedNotice,
            OrderState.Ready => AlreadyStartedNotice,
            _ => null,
        };
        var notices = new List<string>();
        if (notice is not null)
            notices.Add(notice);
        if (notice is not null && answersGiven)
            notices.Add(AnswersNotUsedNotice);
        return await SubmissionAsync(orderId, notices, ct);
    }

    private async Task<OrderSubmission> SubmissionAsync(Guid orderId, IReadOnlyList<string> notices, CancellationToken ct)
    {
        var order = await orders.GetAsync(orderId, ct) ?? throw new OrderAnswerException(OrderNotFoundMessage);
        var view = await BuildViewAsync(order, ct);
        return new OrderSubmission(view.OrderId, view.Display.State, view.CurrentRun?.RunId, notices, view.Clarification);
    }

    private async Task CreateAsync(Func<Task> create)
    {
        try
        {
            await create();
        }
        catch (OrderSourceMissingException)
        {
            throw new OrderSubmissionException(MissingSourceMessage);
        }
    }

    private async Task AddShadowAsync(Guid orderId, DateTimeOffset at, CancellationToken ct)
    {
        try
        {
            await shadows.AddPendingAsync(orderId, at, ct);
            intakeSignal?.Wake();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The shadow intake of order {OrderId} could not be queued.", orderId);
        }
    }

    private async Task<OrderEntity?> GetOwnedAsync(Guid orderId, string? requestingUsername, CancellationToken ct)
    {
        var order = await orders.GetAsync(orderId, ct);
        if (order is null || (requestingUsername is not null && order.CreatedByUser != requestingUsername))
            return null;
        return order;
    }

    private async Task<OrderView> BuildViewAsync(OrderEntity order, CancellationToken ct)
    {
        var orderRuns = await orders.ListRunsAsync(order.Id, ct);
        var current = OrderStateResolver.SelectCurrentRun(orderRuns);
        var display = OrderStateResolver.Resolve(order.State, current?.ToRunState(time.GetUtcNow()));
        var linked = await sources.ListForOrderAsync(order.Id, includeText: false, ct);
        var events = current is null ? [] : await orders.ListRunEventsAsync(current.Id, ct);
        var warnings = current is null ? [] : await warningRepository.ListByRunAsync(current.Id, ct);
        var finishedAt = display.IsFinished ? current?.CompletedAt ?? order.UpdatedAt : (DateTimeOffset?)null;
        var versionStartedAt = current is { RevisionHint: { Length: > 0 } } ? current.CreatedAt : (DateTimeOffset?)null;
        var progress = OrderProgressMapper.Map(display, order.ProgressMode(current), linked.Count > 0, events, order.CreatedAt, finishedAt, time.GetUtcNow(), order.ToIntakeProgress(),
            versionStartedAt);

        return new OrderView(
            order.Id,
            order.RequestText.ToOrderTitle(),
            order.RequestText,
            order.Mode,
            order.Entry,
            order.State,
            display,
            order.CreatedAt,
            order.UpdatedAt,
            current is null ? null : ToRunView(current),
            orderRuns.OrderBy(r => r.OrderVersion ?? 0).ThenBy(r => r.CreatedAt).Select(ToRunView).ToList(),
            linked.Select(i => new OrderSourceView(i.Source.Id, i.Source.FileName, i.Source.Kind, i.Source.PageCount, i.Source.CharCount,
                i.Source.Quality, i.Link.PageSelection, i.Source.Warnings)).ToList(),
            progress,
            warnings,
            BuildClarification(order, linked));
    }

    private ClarificationView? BuildClarification(OrderEntity order, IReadOnlyList<OrderSourceItem> linked)
    {
        var record = IntakeRecordJson.TryParse(order.ClarifyJson);
        if (record is null && order.IntakeOutcome is null)
            return null;

        var after = intakeOptions.Value.NeedsInputAutoStartAfter;
        return new ClarificationView(
            order.IntakeOutcome,
            record?.Output?.TextType,
            record?.Output?.Language,
            record?.Output?.Understanding,
            record?.Assumptions ?? [],
            record?.Questions ?? [],
            IntakeRecordJson.ParseAnswers(order.AnswersJson),
            record?.RevisionSourceId is { } sourceId ? linked.FirstOrDefault(i => i.Source.Id == sourceId)?.Source.FileName : null,
            order.QuestionsAskedAt,
            order.QuestionsAskedAt is { } asked && after > TimeSpan.Zero ? asked + after : null);
    }

    /// <summary>
    /// The uncorrected assumptions of the clarifier that the run's briefing lists as not given by the person (facts of the case
    /// under <c>Intake:ContentAssumptions = OpenPoints</c>); the writer never got them as facts. <c>null</c> without such a block.
    /// </summary>
    private static IReadOnlyList<string>? NotGivenFacts(OrderEntity order, RunEntity run)
    {
        var open = BriefingSections.Parse(run.BriefingText ?? string.Empty).OpenFacts
            .Select(OrderResultRules.AssumptionKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (open.Count == 0)
            return null;
        return (UncorrectedAssumptions(order) ?? [])
            .Select(a => a.Text)
            .Where(t => open.Contains(OrderResultRules.AssumptionKey(t)))
            .ToList();
    }

    /// <summary>The questions the briefing of <paramref name="run"/> lists as left open by the person; <c>null</c> without such a block.</summary>
    private static IReadOnlyList<string>? UnansweredQuestions(RunEntity run) =>
        BriefingSections.Parse(run.BriefingText ?? string.Empty).OpenQuestions is { Count: > 0 } open ? open : null;

    /// <summary>The first version of the order: the lowest version of its standard runs, 1 when none has one.</summary>
    private static int FirstVersion(IReadOnlyList<RunEntity> orderRuns) =>
        orderRuns.Where(r => r.Kind == RunKind.Standard && r.OrderVersion is not null).Select(r => r.OrderVersion!.Value).DefaultIfEmpty(1).Min();

    private static IReadOnlyList<OrderGivenAnswer>? GivenAnswers(OrderEntity order)
    {
        var record = IntakeRecordJson.TryParse(order.ClarifyJson);
        var given = OrderGivenAnswers.From(record?.Questions, record?.Assumptions, IntakeRecordJson.ParseAnswers(order.AnswersJson));
        return given.Count > 0 ? given : null;
    }

    private static IReadOnlyList<IntakeAssumption>? UncorrectedAssumptions(OrderEntity order)
    {
        var record = IntakeRecordJson.TryParse(order.ClarifyJson);
        if (record is null)
            return null;
        var corrected = IntakeRecordJson.ParseAnswers(order.AnswersJson)
            .Where(a => !string.IsNullOrWhiteSpace(a.FreeText))
            .Select(a => a.QuestionId)
            .ToHashSet(StringComparer.Ordinal);
        return record.Assumptions.Where(a => !corrected.Contains(a.Id)).ToList();
    }

    private static OrderRunView ToRunView(RunEntity run) =>
        new(run.Id, run.OrderVersion, run.Kind, run.Status, run.Outcome, run.CreatedAt, run.StartedAt, run.CompletedAt, run.WordCount,
            RevisionBrief.Requested(run.RevisionHint));

    private async Task<SourcePlan> PlanSourcesAsync(IReadOnlyList<OrderSourceSelection> selections, string owner, SourceOptions options, CancellationToken ct)
    {
        if (selections.Count == 0)
            return new SourcePlan([], SourceBudgetPlanner.Plan([], options));

        var needText = selections.Any(s => !string.IsNullOrWhiteSpace(s.PageSelection));
        var found = (await sources.GetManyAsync(selections.Select(s => s.SourceId).ToList(), needText, ct)).ToDictionary(s => s.Id);

        var links = new List<OrderSourceEntity>();
        var items = new List<SourceBudgetItem>();
        foreach (var (selection, position) in selections.Select((s, i) => (s, i)))
        {
            if (!found.TryGetValue(selection.SourceId, out var source) || source.OwnerUsername != owner)
                throw new OrderSubmissionException(MissingSourceMessage);

            string? canonical = null;
            var chars = source.CharCount;
            if (!string.IsNullOrWhiteSpace(selection.PageSelection))
            {
                if (source.PageCount is not { } pageCount)
                    throw new OrderSubmissionException($"Pages can only be chosen for PDF files, not for \"{source.FileName}\".");
                if (!PageSelection.TryParse(selection.PageSelection, pageCount, out var pages, out var error))
                    throw new OrderSubmissionException($"Pages for \"{source.FileName}\": {error}");
                canonical = pages!.ToString();
                chars = pages.ApplyTo(source.ExtractedText ?? string.Empty).CharCountWithoutPageMarkers();
            }

            links.Add(new OrderSourceEntity
            {
                OrderId = Guid.Empty,
                SourceId = source.Id,
                Role = selection.Role,
                Position = position,
                PageSelection = canonical,
                AddedAt = DateTimeOffset.MinValue,
            });
            items.Add(new SourceBudgetItem(source.Id, source.FileName, chars, source.PageCount));
        }

        var budget = SourceBudgetPlanner.Plan(items, options);
        return new SourcePlan(links, budget);
    }

    private static List<OrderSourceSelection> Distinct(IReadOnlyList<OrderSourceSelection>? selections) =>
        (selections ?? []).GroupBy(s => s.SourceId).Select(g => g.First()).ToList();

    private static string TooLargeMessage(SourceBudgetPlan budget)
    {
        var candidates = budget.Items.Where(i => i.PageCount is not null).OrderByDescending(i => i.CharCount).Select(i => i.FileName).ToList();
        var choose = candidates.Count > 0
            ? $" Choose pages for: {string.Join(", ", candidates)}."
            : " Please use fewer or shorter files.";
        return Invariant($"Your files are too long to use completely ({budget.TotalChars:N0} characters, the limit is {budget.MapMaxChars:N0}).") + choose;
    }

    private static string TooLargeWithoutPagesMessage(SourceBudgetPlan budget) =>
        Invariant($"Your files are too long to use completely ({budget.TotalChars:N0} characters, the limit is {budget.MapMaxChars:N0}). ") +
        "Pages cannot be chosen here: use fewer or shorter files, or ask the person to start the order in the Atelier web app and choose the pages there. Nothing was started.";

    private async Task NotifyAsync(Guid orderId)
    {
        try
        {
            await notifier.NotifyOrderUpdatedAsync(orderId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live update for order {OrderId} failed.", orderId);
        }
    }

    /// <summary>
    /// The note of a result: when the shown version is not the current one, the sentence that the new version is still being written
    /// or could not be finished; otherwise the note of the display state.
    /// </summary>
    private static string? ShownNote(OrderDisplay display, RunEntity current, RunEntity? shown)
    {
        if (shown is null || shown.Id == current.Id)
            return NoteFor(display);
        return display.IsFinished
            ? string.Format(CultureInfo.InvariantCulture, RevisionLines.NotFinishedFormat, current.OrderVersion, shown.OrderVersion)
            : string.Format(CultureInfo.InvariantCulture, RevisionLines.StillWritingFormat, shown.OrderVersion);
    }

    private static string? NoteFor(OrderDisplay display) => display.State switch
    {
        OrderDisplayState.DeliveredWithOpenPoints => "The text is ready, but some checks did not pass. Please check it before you use it.",
        OrderDisplayState.Failed => "No text was written.",
        OrderDisplayState.Stopped => display.HasText ? "The order was stopped; this is the last text." : "The order was stopped.",
        OrderDisplayState.Delivered => null,
        OrderDisplayState.NeedsInput => NeedsAnswersNote,
        _ => "The text is not ready yet.",
    };

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record SourcePlan(IReadOnlyList<OrderSourceEntity> Links, SourceBudgetPlan Budget);
}
