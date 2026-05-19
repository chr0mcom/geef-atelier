using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// Base implementation of <see cref="ICrewService"/> that returns empty/no-op results.
/// Subclass and override only the methods your test needs.
/// </summary>
internal abstract class StubCrewServiceBase : ICrewService
{
    public virtual Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ReviewerProfile>>([]);
    public virtual Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken ct = default)
        => Task.FromResult<ReviewerProfile?>(null);
    public virtual Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task DeleteCustomReviewerProfileAsync(string name, CancellationToken ct = default)
        => Task.CompletedTask;
    public virtual Task<string> RenameCustomReviewerProfileAsync(string o, string n, CancellationToken ct = default)
        => Task.FromResult(n);

    public virtual Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ExecutorProfile>>([]);
    public virtual Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken ct = default)
        => Task.FromResult<ExecutorProfile?>(null);
    public virtual Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task DeleteCustomExecutorProfileAsync(string name, CancellationToken ct = default)
        => Task.CompletedTask;
    public virtual Task<string> RenameCustomExecutorProfileAsync(string o, string n, CancellationToken ct = default)
        => Task.FromResult(n);

    public virtual Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<AdvisorProfile>>([]);
    public virtual Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken ct = default)
        => Task.FromResult<AdvisorProfile?>(null);
    public virtual Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken ct = default)
        => Task.CompletedTask;
    public virtual Task<string> RenameCustomAdvisorProfileAsync(string o, string n, CancellationToken ct = default)
        => Task.FromResult(n);

    public virtual Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<GroundingProviderProfile>>([]);
    public virtual Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(string name, CancellationToken ct = default)
        => Task.FromResult<GroundingProviderProfile?>(null);
    public virtual Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(GroundingProviderProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken ct = default)
        => Task.CompletedTask;
    public virtual Task<string> RenameCustomGroundingProviderProfileAsync(string o, string n, CancellationToken ct = default)
        => Task.FromResult(n);

    public virtual Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(bool includeSystem = true, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<FinalizerProfile>>([]);
    public virtual Task<FinalizerProfile?> GetFinalizerProfileAsync(string name, CancellationToken ct = default)
        => Task.FromResult<FinalizerProfile?>(null);
    public virtual Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(FinalizerProfile p, CancellationToken ct = default)
        => Task.FromResult(p);
    public virtual Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken ct = default)
        => Task.CompletedTask;
    public virtual Task<string> RenameCustomFinalizerProfileAsync(string o, string n, CancellationToken ct = default)
        => Task.FromResult(n);

    public virtual Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CrewTemplate>>([]);
    public virtual Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken ct = default)
        => Task.FromResult<CrewTemplate?>(null);
    public virtual Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default)
        => Task.FromResult(t);
    public virtual Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate t, CancellationToken ct = default)
        => Task.FromResult(t);
    public virtual Task DeleteCustomCrewTemplateAsync(string name, CancellationToken ct = default)
        => Task.CompletedTask;
    public virtual Task<string> RenameCustomCrewTemplateAsync(string o, string n, CancellationToken ct = default)
        => Task.FromResult(n);

    public virtual Task<CrewSnapshot> ResolveSnapshotAsync(
        string? crewTemplateName, CrewSpec? customCrew, CancellationToken ct = default) =>
        Task.FromResult(new CrewSnapshot(
            1, "klassik", SystemCrew.DefaultExecutorProfile, [], EvaluationStrategy.Parallel, null, []));
}
