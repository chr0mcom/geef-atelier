using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
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

    /// <summary>Lists reviewer profiles. Custom profiles always; built-in system profiles only when <paramref name="includeSystem"/> is true.</summary>
    Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the reviewer profile (system or custom) with the exact <paramref name="name"/>, or null if none exists.</summary>
    Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom reviewer profile. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom reviewer profile. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom reviewer profile by name. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task DeleteCustomReviewerProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a custom reviewer profile, cascading the new name into every custom crew template
    /// that references it. Returns the final (custom-prefixed) name. Throws for system profiles
    /// or when the target name is already taken.
    /// </summary>
    Task<string> RenameCustomReviewerProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    // --- Executor profiles ---

    /// <summary>Lists executor profiles. Custom profiles always; built-in system profiles only when <paramref name="includeSystem"/> is true.</summary>
    Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the executor profile (system or custom) with the exact <paramref name="name"/>, or null if none exists.</summary>
    Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom executor profile. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom executor profile. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom executor profile by name. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task DeleteCustomExecutorProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a custom executor profile, cascading the new name into every custom crew template
    /// that references it. Returns the final (custom-prefixed) name. Throws for system profiles
    /// or when the target name is already taken.
    /// </summary>
    Task<string> RenameCustomExecutorProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    // --- Advisor profiles ---

    /// <summary>Lists advisor profiles. Custom profiles always; built-in system profiles only when <paramref name="includeSystem"/> is true.</summary>
    Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the advisor profile (system or custom) with the exact <paramref name="name"/>, or null if none exists.</summary>
    Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom advisor profile. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom advisor profile. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom advisor profile by name. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a custom advisor profile, cascading the new name into every custom crew template
    /// that references it. Returns the final (custom-prefixed) name. Throws for system profiles
    /// or when the target name is already taken.
    /// </summary>
    Task<string> RenameCustomAdvisorProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    // --- Grounding-provider profiles ---

    /// <summary>Lists grounding-provider profiles. Custom profiles always; built-in system profiles only when <paramref name="includeSystem"/> is true.</summary>
    Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the grounding-provider profile (system or custom) with the exact <paramref name="name"/>, or null if none exists.</summary>
    Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom grounding-provider profile. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom grounding-provider profile. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom grounding-provider profile by name. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a custom grounding-provider profile, cascading the new name into every custom crew
    /// template that references it. Returns the final (custom-prefixed) name. Throws for system
    /// profiles or when the target name is already taken.
    /// </summary>
    Task<string> RenameCustomGroundingProviderProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    // --- Finalizer profiles ---

    /// <summary>Lists finalizer profiles. Custom profiles always; built-in system profiles only when <paramref name="includeSystem"/> is true.</summary>
    Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the finalizer profile (system or custom) with the exact <paramref name="name"/>, or null if none exists.</summary>
    Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom finalizer profile. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom finalizer profile. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile profile, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom finalizer profile by name. Throws <see cref="InvalidOperationException"/> for system profiles.</summary>
    Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a custom finalizer profile, cascading the new name into every custom crew template
    /// that references it. Returns the final (custom-prefixed) name. Throws for system profiles
    /// or when the target name is already taken.
    /// </summary>
    Task<string> RenameCustomFinalizerProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    // --- Crew templates ---

    /// <summary>Lists crew templates. Custom templates always; built-in system templates (klassik, juristisch, akademisch, marketing) only when <paramref name="includeSystem"/> is true.</summary>
    Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the crew template (system or custom) with the exact <paramref name="name"/>, or null if none exists.</summary>
    Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Creates a custom crew template. The name is auto-prefixed with <c>"custom-"</c> if not already present.</summary>
    Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Updates a custom crew template. Throws <see cref="InvalidOperationException"/> for system templates.</summary>
    Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Deletes a custom crew template by name. Throws <see cref="InvalidOperationException"/> for system templates.</summary>
    Task DeleteCustomCrewTemplateAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Renames a custom crew template, cascading the new name into <c>CrewTemplateName</c> of past
    /// runs (frozen snapshots stay untouched). Returns the final (custom-prefixed) name. Throws for
    /// system templates or when the target name is already taken.
    /// </summary>
    Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken cancellationToken = default);

    // --- Snapshot ---

    /// <summary>
    /// Resolves a <see cref="CrewSnapshot"/> from a named template or an inline spec.
    /// When both are null, falls back to the <c>"klassik"</c> system template.
    /// </summary>
    Task<CrewSnapshot> ResolveSnapshotAsync(string? crewTemplateName, CrewSpec? customCrew, CancellationToken cancellationToken = default);
}
