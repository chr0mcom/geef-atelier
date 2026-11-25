using Geef.Atelier.Core.Domain.Intake;
using Geef.Atelier.Core.Domain.Orders;

namespace Geef.Atelier.Application.Orders;

/// <summary>A start of the first run of an order that leaves the intake (<see cref="OrderState.Analyzing"/> or <see cref="OrderState.NeedsInput"/>).</summary>
internal sealed record RunStartRequest(
    OrderEntity Order,
    OrderState Expected,
    IntakeRecord? Record,
    IReadOnlyList<OrderAnswer> Answers,
    IntakeOutcome? Outcome,
    bool RecordIsNew);

internal interface IOrderRunStarter
{
    /// <summary>Resolves the snapshot (as in stage 1), composes the brief (<see cref="IOrderBriefComposer"/>), applies the revision role, starts run v1 atomically (<c>TryStartWithRunAsync</c>), reports model-assignment and intake warnings and notifies. Returns the run id, or null when the order was no longer in <see cref="RunStartRequest.Expected"/>.</summary>
    Task<Guid?> TryStartAsync(RunStartRequest request, CancellationToken ct);
}
