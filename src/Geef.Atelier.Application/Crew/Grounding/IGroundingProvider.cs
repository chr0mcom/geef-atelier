using Geef.Atelier.Core.Domain.Crew.Grounding;

namespace Geef.Atelier.Application.Crew.Grounding;

/// <summary>
/// Abstracts a single grounding-provider implementation (e.g. Tavily web-search, vector store).
/// Implementations live in the Infrastructure layer; this interface belongs to Application so that
/// the pipeline can use it without depending on Infrastructure.
/// </summary>
public interface IGroundingProvider
{
    /// <summary>
    /// Discriminator that matches <see cref="GroundingProviderProfile.ProviderType"/>.
    /// Used by <see cref="IGroundingProviderFactory"/> for resolution.
    /// </summary>
    string ProviderType { get; }

    /// <summary>
    /// Enriches the briefing by calling the external data source described by <paramref name="profile"/>.
    /// Persists a <see cref="GroundingConsultation"/> as a side-effect.
    /// </summary>
    Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct);
}
