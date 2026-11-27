using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Profiles;
using Geef.Atelier.Core.Persistence.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Application.Orders;

internal sealed class OrderRunStarter(
    IOrderRepository orders,
    ISourceRepository sources,
    IUserProfileRepository profiles,
    ICrewService crewService,
    IOrderBriefComposer briefComposer,
    IRunWarningSink warningSink,
    IOrderNotifier notifier,
    ILogger<OrderRunStarter> logger,
    TimeProvider time,
    IOptions<OrderOptions>? orderOptions = null) : IOrderRunStarter
{
    public async Task<Guid?> TryStartAsync(RunStartRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Order);
        if (request.Expected is not (OrderState.Analyzing or OrderState.NeedsInput))
            throw new ArgumentException("Only orders in the intake can be started this way.", nameof(request));

        var order = request.Order;
        var answers = request.Answers ?? [];
        var linked = await sources.ListForOrderAsync(order.Id, includeText: false, ct);
        var profile = string.IsNullOrWhiteSpace(order.CreatedByUser) ? null : await profiles.GetAsync(order.CreatedByUser, ct);
        var outline = orderOptions?.Value.CarefulOutlineFor(order.CreatedByUser) == CarefulOutlineMode.On;
        var resolved = await crewService.ResolveOrderSnapshotAsync(order.Mode, order.CrewTemplateName, null, linked.Count > 0, ct, outline);

        var now = time.GetUtcNow();
        var briefing = briefComposer.Compose(new OrderBriefInput(order, request.Record, answers, profile,
            AssumptionsShown: request.Expected == OrderState.NeedsInput, NotChecked: request.Record?.Output is null)).Text;
        var run = resolved.ToFirstRun(order, briefing, now);
        var start = new OrderStart(
            order.Id,
            request.Expected,
            run,
            request.RecordIsNew && request.Record is not null ? IntakeRecordJson.Serialize(request.Record) : null,
            answers.Count > 0 ? IntakeRecordJson.SerializeAnswers(answers) : null,
            request.Outcome,
            RoleChanges(order, request.Record, answers, linked),
            now);

        if (!await orders.TryStartWithRunAsync(start, ct))
        {
            logger.LogInformation("Order {OrderId} was no longer {State}; the start was dropped.", order.Id, request.Expected);
            return null;
        }

        await warningSink.ReportAssignmentWarningsAsync(run.Id, resolved.Warnings);
        switch (request.Outcome)
        {
            case IntakeOutcome.Failed:
                await warningSink.ReportAsync(run.Id, RunWarningSources.Intake, RunWarningCodes.IntakeFailed, IntakeWarningMessages.Failed);
                break;
            case IntakeOutcome.AutoStarted:
                await warningSink.ReportAsync(run.Id, RunWarningSources.Intake, RunWarningCodes.IntakeUnanswered, IntakeWarningMessages.Unanswered);
                break;
        }

        try
        {
            await notifier.NotifyOrderUpdatedAsync(order.Id, CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Live update for order {OrderId} failed.", order.Id);
        }

        return run.Id;
    }

    /// <summary>
    /// The file the intake chose as the text to revise becomes <see cref="SourceRole.TemplateToRevise"/>, but only in the direct
    /// path (the only one that revises a given text) and not when the person answered "no" to the revision guess; then a file
    /// that already carries the role goes back to <see cref="SourceRole.Facts"/>.
    /// </summary>
    private static IReadOnlyList<(Guid SourceId, SourceRole Role)> RoleChanges(
        OrderEntity order, IntakeRecord? record, IReadOnlyList<OrderAnswer> answers, IReadOnlyList<OrderSourceItem> linked)
    {
        if (record?.RevisionSourceId is not { } sourceId)
            return [];
        var link = linked.FirstOrDefault(i => i.Source.Id == sourceId)?.Link;
        if (link is null)
            return [];

        var declined = answers.Any(a => a.QuestionId == RevisionAnswer.QuestionId && string.Equals(a.OptionId, RevisionAnswer.No, StringComparison.Ordinal));
        if (declined)
            return link.Role == SourceRole.TemplateToRevise ? [(sourceId, SourceRole.Facts)] : [];
        return order.Mode == OrderMode.Direct && link.Role != SourceRole.TemplateToRevise ? [(sourceId, SourceRole.TemplateToRevise)] : [];
    }
}
