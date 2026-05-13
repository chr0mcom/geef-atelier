using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Application.Crew;

/// <summary>
/// Application-service contract for managing crew profiles and templates, and building run snapshots.
/// System profiles are read-only; custom profiles are auto-prefixed with <c>"custom-"</c>.
/// </summary>
public interface ICrewService
{
    // --- Reviewer profiles ---

    Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);
    Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom reviewer profile. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom reviewer profile. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom reviewer profile by name. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task DeleteCustomReviewerProfileAsync(string name, CancellationToken cancellationToken = default);

    // --- Executor profiles ---

    Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);
    Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom executor profile. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom executor profile. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom executor profile by name. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task DeleteCustomExecutorProfileAsync(string name, CancellationToken cancellationToken = default);

    // --- Advisor profiles ---

    Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);
    Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom advisor profile. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom advisor profile. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom advisor profile by name. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken cancellationToken = default);

    // --- Grounding-provider profiles ---

    Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);
    Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom grounding-provider profile. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom grounding-provider profile. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom grounding-provider profile by name. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken cancellationToken = default);

    // --- Crew templates ---

    Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);
    Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom crew template. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom crew template. Throws <see cref="InvalidOperationException"/> for system templates.</summary>
    Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom crew template by name. Throws <see cref="InvalidOperationException"/> for system templates.</summary>
    Task DeleteCustomCrewTemplateAsync(string name, CancellationToken cancellationToken = default);

    // --- Snapshot ---

    /// <summary>
    /// Resolves a <see cref="CrewSnapshot"/> from a named template or an inline spec.
    /// When both are null, falls back to the <c>"klassik"</c> system template.
    /// </summary>
    Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken cancellationToken = default);
}
