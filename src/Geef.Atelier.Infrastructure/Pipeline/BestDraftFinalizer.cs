using Geef.Atelier.Core.Domain;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;
using SdkSeverity = Geef.Sdk.Results.FindingSeverity;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Finalizes the best round (<see cref="LoopRoundExtensions.BestRound"/>) instead of the one handed over when the last round still
/// has blocking findings (D-085, <c>Convergence:BestDraft</c>). An approved loop ends with a round without blocking findings and is
/// finalized as it is. When it picks a round, that round's number goes to <see cref="AtelierContextKeys.DeliveredRound"/> of the final
/// context, so the stored iteration names the round the text comes from.
/// </summary>
internal sealed class BestDraftFinalizer(IFinalizer<FinalizedDocument> inner, SdkSeverity threshold) : IFinalizer<FinalizedDocument>
{
    /// <inheritdoc/>
    public Task<FinalizeResult<FinalizedDocument>> FinalizeAsync(IRunContext context, CancellationToken cancellationToken)
    {
        if (!context.TryGet(GeefKeys.IterationHistory, out var history)
            || history is null
            || history.Records.Count == 0
            || history.Records[^1].EvaluationResult.BlockingCount(threshold) == 0
            || history.BestRound(threshold) is not { Context: { } bestContext } best)
            return inner.FinalizeAsync(context, cancellationToken);

        return inner.FinalizeAsync(bestContext.Set(AtelierContextKeys.DeliveredRound, best.Iteration), cancellationToken);
    }
}
