using System.Globalization;
using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Models;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Orders;
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
    ISourceIntakeService? intake = null,
    IOrderNotifier? notifier = null,
    TimeProvider? timeProvider = null) : IOrderService
{
    internal const string EmptyRequestMessage = "Please describe what you need.";
    internal const string MissingSourceMessage = "One of the files was not found. Please add it again.";
    internal const string StillWorkingMessage = "This order is still being worked on. Stop it first.";
    internal const string MapNotice = "Your files are long, so the writer gets a map of them and reads the parts that matter.";
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly RunStatus[] ActiveStatuses = [RunStatus.Pending, RunStatus.Running];

    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

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

        var selections = (request.Sources ?? [])
            .GroupBy(s => s.SourceId)
            .Select(g => g.First())
            .ToList();
        if (selections.Count > srcOptions.MaxFilesPerOrder)
            throw new OrderSubmissionException(Invariant($"You can use at most {srcOptions.MaxFilesPerOrder} files per order."));

        var plan = await PlanSourcesAsync(selections, request.CreatedByUser, srcOptions, ct);
        var notices = new List<string>();
        if (plan.Budget.Class == SourceBudgetClass.WithMap)
            notices.Add(MapNotice);

        var now = _time.GetUtcNow();
        var order = new OrderEntity
        {
            Id = Guid.NewGuid(),
            CreatedByUser = request.CreatedByUser,
            RequestText = text,
            Mode = request.Mode,
            State = OrderState.Ready,
            Entry = request.Entry,
            CrewTemplateName = request.Mode == OrderMode.Careful ? request.CrewTemplateName : null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        var resolved = await ResolveSnapshotAsync(request, hasSources: plan.Links.Count > 0, ct);
        var run = new RunEntity
        {
            Id = Guid.NewGuid(),
            CreatedAt = now,
            Status = RunStatus.Pending,
            BriefingText = briefComposer.Compose(order),
            ConfigJson = "{}",
            CreatedByUser = request.CreatedByUser,
            CrewTemplateName = resolved.Snapshot.TemplateName,
            CrewSnapshot = JsonSerializer.Serialize(resolved.Snapshot, SnapshotJsonOptions),
            TokensTotal = 0,
            CostTotal = 0m,
            Kind = RunKind.Standard,
            OrderId = order.Id,
            OrderVersion = 1,
        };

        var links = plan.Links.Select(l => l with { OrderId = order.Id, AddedAt = now }).ToList();
        await orders.CreateWithFirstRunAsync(order, links, run, ct);
        await ReportAssignmentWarningsAsync(run.Id, resolved.Warnings);
        await NotifyAsync(order.Id);

        return new OrderSubmission(order.Id, OrderDisplayState.Queued, run.Id, notices);
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
        var display = OrderStateResolver.Resolve(order.State, current?.ToRunState());
        if (current is null)
            return new OrderResult(orderId, display, null, null, null, null, [], [], [], [], NoteFor(display));

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
            NoteFor(display));
    }

    public async Task<IReadOnlyList<OrderListItem>> ListAsync(string? ownerUsername, int limit = 50, CancellationToken ct = default)
    {
        if (limit <= 0)
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");

        var rows = await orders.ListAsync(ownerUsername, limit, ct);
        return rows.Select(r =>
        {
            var display = OrderStateResolver.Resolve(r.Order.State, r.CurrentRun?.ToRunState());
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
        var deadline = _time.GetUtcNow() + wait;

        while (true)
        {
            var view = await GetAsync(orderId, requestingUsername, ct);
            if (view is null || view.Display.IsFinished || knownState is null || view.Display.State != knownState)
                return view;

            var remaining = deadline - _time.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return view;
            await Task.Delay(remaining < poll ? remaining : poll, _time, ct);
        }
    }

    public async Task<bool> CancelAsync(Guid orderId, string? requestingUsername, CancellationToken ct = default)
    {
        var order = await GetOwnedAsync(orderId, requestingUsername, ct);
        if (order is null)
            return false;

        var current = OrderStateResolver.SelectCurrentRun(await orders.ListRunsAsync(orderId, ct));
        bool changed;
        if (current is null)
        {
            changed = order.State is not OrderState.Cancelled
                      && await orders.TrySetStateAsync(orderId, order.State, OrderState.Cancelled, ct);
        }
        else
        {
            changed = ActiveStatuses.Contains(current.Status) && await runs.RequestCancellationAsync(current.Id, ct);
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
            throw new InvalidOperationException(StillWorkingMessage);

        var linked = await sources.ListForOrderAsync(orderId, includeText: false, ct);
        if (!await orders.DeleteAsync(orderId, ct))
            return false;

        foreach (var item in linked.Where(i => !i.Source.InLibrary))
        {
            try
            {
                if (intake is not null)
                    await intake.DeleteAsync(item.Source.Id, null, ct);
                else
                    await sources.DeleteIfUnattachedAsync(item.Source.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Source {SourceId} of deleted order {OrderId} could not be deleted; the cleanup will retry.", item.Source.Id, orderId);
            }
        }

        await NotifyAsync(orderId);
        return true;
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
        var display = OrderStateResolver.Resolve(order.State, current?.ToRunState());
        var linked = await sources.ListForOrderAsync(order.Id, includeText: false, ct);
        var events = current is null ? [] : await orders.ListRunEventsAsync(current.Id, ct);
        var warnings = current is null ? [] : await warningRepository.ListByRunAsync(current.Id, ct);
        var finishedAt = display.IsFinished ? current?.CompletedAt ?? order.UpdatedAt : (DateTimeOffset?)null;
        var progress = OrderProgressMapper.Map(display, order.Mode, linked.Count > 0, events, order.CreatedAt, finishedAt, _time.GetUtcNow());

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
            warnings);
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
                chars = PageMarkers.StripMarkers(pages.ApplyTo(source.ExtractedText ?? string.Empty)).Length;
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
        if (budget.Class == SourceBudgetClass.TooLarge)
        {
            var candidates = items.Where(i => i.PageCount is not null).OrderByDescending(i => i.CharCount).Select(i => i.FileName).ToList();
            var choose = candidates.Count > 0
                ? $" Choose pages for: {string.Join(", ", candidates)}."
                : " Please use fewer or shorter files.";
            throw new OrderSubmissionException(Invariant(
                $"Your files are too long to use completely ({budget.TotalChars:N0} characters, the limit is {budget.MapMaxChars:N0}).") + choose);
        }

        return new SourcePlan(links, budget);
    }

    private async Task<ResolvedCrewSnapshot> ResolveSnapshotAsync(SubmitOrderRequest request, bool hasSources, CancellationToken ct)
    {
        if (request.Mode == OrderMode.Direct)
            return await crewService.ResolveCodeTemplateSnapshotAsync(SystemCrew.DirectTemplate, ct);

        var resolved = await crewService.ResolveSnapshotWithWarningsAsync(
            request.CustomCrew is null ? request.CrewTemplateName ?? SystemCrew.KlassikTemplateName : request.CrewTemplateName,
            request.CustomCrew,
            ct);
        if (!hasSources)
            return resolved;

        var providers = (resolved.Snapshot.GroundingProviders ?? [])
            .Where(p => p.Name != SystemCrew.RunAttachmentsProfile.Name && p.Name != SystemCrew.OrderSourcesProfile.Name)
            .ToList();
        return resolved with { Snapshot = resolved.Snapshot with { GroundingProviders = [SystemCrew.OrderSourcesProfile, .. providers] } };
    }

    private async Task ReportAssignmentWarningsAsync(Guid runId, IReadOnlyList<ModelAssignmentWarning> warnings)
    {
        foreach (var warning in warnings)
            await warningSink.ReportAsync(runId, RunWarningSources.ModelAssignment, RunWarningCodes.ModelAssignment,
                $"The AI model for \"{warning.ShownName}\" could not be chosen as planned, so a different one was used.");
    }

    private async Task NotifyAsync(Guid orderId)
    {
        if (notifier is null)
            return;
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
        _ => "The text is not ready yet.",
    };

    private static string Invariant(FormattableString value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed record SourcePlan(IReadOnlyList<OrderSourceEntity> Links, SourceBudgetPlan Budget);
}
