using Geef.Atelier.Core.Domain.Crew.Specialization;

namespace Geef.Atelier.Application.Crew;

/// <summary>Result of a generality review when promoting/generalizing a specialization pack.</summary>
/// <param name="Approved">True when the pack's text is free of one-off specifics and safe to generalize.</param>
/// <param name="Concerns">Human-readable reasons when not approved (one-off references to fix).</param>
public sealed record GeneralityReviewResult(bool Approved, IReadOnlyList<string> Concerns);

/// <summary>
/// LLM-backed check that a pack's <see cref="SpecializationPack.SpecializationText"/> is general enough
/// to be reused beyond a single crew (no task-specific one-off references), gating Promote/Clone-to-Generalize.
/// </summary>
public interface IPackGeneralityReviewer
{
    /// <summary>Reviews <paramref name="pack"/> for generalization to <paramref name="targetScope"/>.</summary>
    Task<GeneralityReviewResult> ReviewAsync(SpecializationPack pack, PackScope targetScope, CancellationToken ct = default);
}
