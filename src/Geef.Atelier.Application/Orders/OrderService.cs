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
            throw new OrderSubmissionException(TooLargeMessage(plan.Budget));
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

        var resolved = await crewService.ResolveOrderSnapshotAsync(request.Mode, request.CrewTemplateName, request.CustomCrew, links.Count > 0, ct);
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

    public async Task<OrderResult?> GetResultAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        var order = await GetOwnedAsync(orderId, requestingUsername, ct);
        if (order is null)
            return null;

        var orderRuns = await orders.ListRunsAsync(orderId, ct);
        var current = OrderStateResolver.SelectCurrentRun(orderRuns);
        var display = OrderStateResolver.Resolve(order.State, current?.ToRunState(time.GetUtcNow()));
        if (current is null)
            return new OrderResult(orderId, display, null, null, null, null, [], [], [], [], NoteFor(display), UncorrectedAssumptions(order));

        var report = current.ReportJson is null ? null : RunReportJson.TryParse(current.ReportJson);
        var usages = await sources.ListRunUsagesAsync(current.Id, ct);
        var warnings = await warningRepository.ListByRunAsync(current.Id, ct);
        var hasText = !string.IsNullOrWhiteSpace(current.FinalText);

        return new OrderResult(
            orderId,
            display,
            current.Id,
            current.OrderVersion,
            hasText ? current.FinalText : null,
            hasText ? current.WordCount : null,
            report?.Placeholders ?? [],
            report?.Checks.Where(c => !c.Passed).ToList() ?? [],
            usages.Select(u => new OrderSourceUsageView(u.FileName, u.Mode, u.CharsIncluded, u.PagesIncluded)).ToList(),
            warnings,
            NoteFor(display),
            UncorrectedAssumptions(order));
    }

    public async Task<IReadOnlyList<OrderListItem>> ListAsync(string? ownerUsername, int limit = 50, CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");

        var rows = await orders.ListAsync(ownerUsername, limit, ct);
        return rows.Select(r =>
        {
            var display = OrderStateResolver.Resolve(r.Order.State, r.CurrentRun?.ToRunState(time.GetUtcNow()));
            return new OrderListItem(
                r.Order.Id,
                r.Order.RequestText.ToOrderTitle(),
                display,
                r.Order.Mode,
                r.Order.CreatedAt,
                r.Order.UpdatedAt,
                r.SourceCount,
                display.HasText ? r.CurrentRun?.WordCount : null);
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
        var progress = OrderProgressMapper.Map(display, order.Mode, linked.Count > 0, events, order.CreatedAt, finishedAt, time.GetUtcNow(), order.ToIntakeProgress());

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
        new(run.Id, run.OrderVersion, run.Kind, run.Status, run.Outcome, run.CreatedAt, run.StartedAt, run.CompletedAt, run.WordCount);

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
