using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.FactCheck;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

/// <summary>
/// One <c>check_facts</c> call: the proposed findings (<c>null</c> for an invalid answer or a refusal), the raw answer, the model and,
/// after a failure, its error code; <see cref="Dropped"/> counts findings of the answer the parser had to leave out.
/// </summary>
internal sealed record FactCheckCall(IReadOnlyList<ProposedFactFinding>? Proposed, string? RawJson, int DurationMs, string Model, string? ErrorCode, int Dropped = 0);

/// <summary>One <c>recheck_facts</c> call: the answers per finding (<c>null</c> for an invalid answer), the raw answer, the model and, after a failure, its error code.</summary>
internal sealed record RecheckCall(IReadOnlyList<RecheckResult>? Results, string? RawJson, int DurationMs, string Model, string? ErrorCode);

/// <summary>Asks a model of another family than the writer to check a finished text against its material.</summary>
internal interface IFactChecker
{
    /// <summary>
    /// Checks <paramref name="draft"/> against <paramref name="material"/> with the model of <paramref name="profile"/>, at most for
    /// <paramref name="timeout"/>; the costs are booked on <paramref name="iteration"/>. Failures, the time limit included, come back as
    /// <see cref="FactCheckCall.ErrorCode"/>, never as an exception.
    /// </summary>
    Task<FactCheckCall> CheckAsync(FactMaterial material, string draft, ReviewerProfile profile, TimeSpan timeout, int iteration, CancellationToken ct);

    /// <summary>
    /// Asks whether the findings of round 1 in <paramref name="previous"/> are still in the revised <paramref name="draft"/>, at most for
    /// <paramref name="timeout"/>.
    /// </summary>
    Task<RecheckCall> RecheckAsync(FactMaterial material, string draft, IReadOnlyList<FactFinding> previous, ReviewerProfile profile, TimeSpan timeout, CancellationToken ct);
}
