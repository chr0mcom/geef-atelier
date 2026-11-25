using Geef.Atelier.Core.Domain.Research;

namespace Geef.Atelier.Infrastructure.Research;

/// <summary>
/// Passages the model proposed, what the parser dropped (per reason), the model used and an error code when the call failed (then
/// <see cref="Passages"/> is empty).
/// </summary>
internal sealed record CondenseOutcome(IReadOnlyList<ProposedPassage> Passages, IReadOnlyDictionary<ResearchDropReason, int> ParserDropped,
    string? Model, string? ErrorCode, int DurationMs);

/// <summary>Picks passages that answer the topics from the raw page texts in one AI call; sees nothing of the order but the topics.</summary>
internal interface IDossierCondenser
{
    /// <summary>Proposes passages for <paramref name="topics"/> from <paramref name="pages"/>; never throws for call failures.</summary>
    Task<CondenseOutcome> CondenseAsync(IReadOnlyList<string> topics, IReadOnlyList<ResearchPage> pages, CancellationToken ct);
}
