using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Application.Crew;

internal sealed class CrewService(
    IReviewerProfileRepository reviewerRepo,
    IExecutorProfileRepository executorRepo,
    IAdvisorProfileRepository advisorRepo,
    IGroundingProviderProfileRepository groundingRepo,
    ICrewTemplateRepository templateRepo) : ICrewService
{
    private const string ReadOnlyMessage = "System profile is read-only — copy it as a custom variant.";
    private const string ReadOnlyAdvisorMessage = "System advisor profile is read-only — copy it as a custom variant.";
    private const string ReadOnlyGroundingMessage = "System grounding-provider profile is read-only — copy it as a custom variant.";
    private const string ReadOnlyTemplateMessage = "System template is read-only — copy it as a custom variant.";

    // --- Reviewer profiles ---

    public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
        => reviewerRepo.ListAsync(includeSystem, cancellationToken);

    public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken cancellationToken = default)
        => reviewerRepo.GetByNameAsync(name, cancellationToken);

    public async Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default)
    {
        var baseName = SystemCrew.EnsureCustomPrefix(profile.Name);
        var uniqueName = await UniqueNameAsync(baseName, n => reviewerRepo.GetByNameAsync(n, cancellationToken));
        var normalized = profile with { Name = uniqueName, IsSystem = false };
        await reviewerRepo.CreateAsync(normalized, cancellationToken);
        return normalized;
    }

    public async Task<ReviewerProfile> UpdateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemName(profile.Name))
            throw new InvalidOperationException(ReadOnlyMessage);
        await reviewerRepo.UpdateAsync(profile, cancellationToken);
        return profile;
    }

    public Task DeleteCustomReviewerProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemName(name))
            throw new InvalidOperationException(ReadOnlyMessage);
        return reviewerRepo.DeleteAsync(name, cancellationToken);
    }

    // --- Executor profiles ---

    public Task<IReadOnlyList<ExecutorProfile>> ListExecutorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
        => executorRepo.ListAsync(includeSystem, cancellationToken);

    public Task<ExecutorProfile?> GetExecutorProfileAsync(string name, CancellationToken cancellationToken = default)
        => executorRepo.GetByNameAsync(name, cancellationToken);

    public async Task<ExecutorProfile> CreateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default)
    {
        var baseName = SystemCrew.EnsureCustomPrefix(profile.Name);
        var uniqueName = await UniqueNameAsync(baseName, n => executorRepo.GetByNameAsync(n, cancellationToken));
        var normalized = profile with { Name = uniqueName, IsSystem = false };
        await executorRepo.CreateAsync(normalized, cancellationToken);
        return normalized;
    }

    public async Task<ExecutorProfile> UpdateCustomExecutorProfileAsync(ExecutorProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemName(profile.Name))
            throw new InvalidOperationException(ReadOnlyMessage);
        await executorRepo.UpdateAsync(profile, cancellationToken);
        return profile;
    }

    public Task DeleteCustomExecutorProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemName(name))
            throw new InvalidOperationException(ReadOnlyMessage);
        return executorRepo.DeleteAsync(name, cancellationToken);
    }

    // --- Advisor profiles ---

    public Task<IReadOnlyList<AdvisorProfile>> ListAdvisorProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
        => advisorRepo.ListAsync(includeSystem, cancellationToken);

    public Task<AdvisorProfile?> GetAdvisorProfileAsync(string name, CancellationToken cancellationToken = default)
        => advisorRepo.GetByNameAsync(name, cancellationToken);

    public async Task<AdvisorProfile> CreateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemAdvisorName(profile.Name))
            throw new InvalidOperationException(ReadOnlyAdvisorMessage);
        var baseName = SystemCrew.EnsureCustomPrefix(profile.Name);
        var uniqueName = await UniqueNameAsync(baseName, n => advisorRepo.GetByNameAsync(n, cancellationToken));
        var normalized = profile with { Name = uniqueName, IsSystem = false };
        await advisorRepo.CreateAsync(normalized, cancellationToken);
        return normalized;
    }

    public async Task<AdvisorProfile> UpdateCustomAdvisorProfileAsync(AdvisorProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemAdvisorName(profile.Name))
            throw new InvalidOperationException(ReadOnlyAdvisorMessage);
        await advisorRepo.UpdateAsync(profile, cancellationToken);
        return profile;
    }

    public Task DeleteCustomAdvisorProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemAdvisorName(name))
            throw new InvalidOperationException(ReadOnlyAdvisorMessage);
        return advisorRepo.DeleteAsync(name, cancellationToken);
    }

    // --- Grounding-provider profiles ---

    public async Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(
        bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        var custom = await groundingRepo.ListAsync(cancellationToken);
        if (!includeSystem)
            return custom;
        var system = SystemCrew.GroundingProviderProfiles.Values.ToList();
        return [.. system, .. custom];
    }

    public async Task<GroundingProviderProfile?> GetGroundingProviderProfileAsync(
        string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.GroundingProviderProfiles.TryGetValue(name, out var system))
            return system;
        return await groundingRepo.GetByNameAsync(name, cancellationToken);
    }

    public async Task<GroundingProviderProfile> CreateCustomGroundingProviderProfileAsync(
        GroundingProviderProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemGroundingProviderName(profile.Name))
            throw new InvalidOperationException(ReadOnlyGroundingMessage);
        var baseName = SystemCrew.EnsureCustomPrefix(profile.Name);
        var uniqueName = await UniqueNameAsync(baseName, n => groundingRepo.GetByNameAsync(n, cancellationToken));
        var normalized = profile with { Name = uniqueName, IsSystem = false };
        await groundingRepo.CreateAsync(normalized, cancellationToken);
        return normalized;
    }

    public async Task<GroundingProviderProfile> UpdateCustomGroundingProviderProfileAsync(
        GroundingProviderProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemGroundingProviderName(profile.Name))
            throw new InvalidOperationException(ReadOnlyGroundingMessage);
        await groundingRepo.UpdateAsync(profile, cancellationToken);
        return profile;
    }

    public Task DeleteCustomGroundingProviderProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemGroundingProviderName(name))
            throw new InvalidOperationException(ReadOnlyGroundingMessage);
        return groundingRepo.DeleteAsync(name, cancellationToken);
    }

    // --- Crew templates ---

    public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
        => templateRepo.ListAsync(includeSystem, cancellationToken);

    public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken cancellationToken = default)
        => templateRepo.GetByNameAsync(name, cancellationToken);

    public async Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default)
    {
        var baseName = SystemCrew.EnsureCustomPrefix(template.Name);
        var uniqueName = await UniqueNameAsync(baseName, n => templateRepo.GetByNameAsync(n, cancellationToken));
        var normalized = template with { Name = uniqueName, IsSystem = false };
        await templateRepo.CreateAsync(normalized, cancellationToken);
        return normalized;
    }

    public async Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemName(template.Name))
            throw new InvalidOperationException(ReadOnlyTemplateMessage);
        await templateRepo.UpdateAsync(template, cancellationToken);
        return template;
    }

    public Task DeleteCustomCrewTemplateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemName(name))
            throw new InvalidOperationException(ReadOnlyTemplateMessage);
        return templateRepo.DeleteAsync(name, cancellationToken);
    }

    // --- Helpers ---

    private static async Task<string> UniqueNameAsync<T>(string baseName, Func<string, Task<T?>> existsCheck) where T : class
    {
        if (await existsCheck(baseName) is null)
            return baseName;
        for (var i = 2; ; i++)
        {
            var candidate = $"{baseName}-{i}";
            if (await existsCheck(candidate) is null)
                return candidate;
        }
    }

    // --- Snapshot ---

    public async Task<CrewSnapshot> ResolveSnapshotAsync(
        string? crewTemplateName, CrewSpec? customCrew, CancellationToken cancellationToken = default)
    {
        if (customCrew is not null)
            return await CrewSnapshotBuilder.BuildAsync(
                customCrew,
                (name, ct) => executorRepo.GetByNameAsync(name, ct),
                (name, ct) => reviewerRepo.GetByNameAsync(name, ct),
                (name, ct) => advisorRepo.GetByNameAsync(name, ct),
                (name, ct) => GetGroundingProviderProfileAsync(name, ct),
                cancellationToken);

        var templateName = crewTemplateName ?? SystemCrew.KlassikTemplateName;
        var template = await templateRepo.GetByNameAsync(templateName, cancellationToken)
            ?? throw new InvalidOperationException($"Crew template '{templateName}' not found.");

        return await CrewSnapshotBuilder.BuildAsync(
            template,
            (name, ct) => executorRepo.GetByNameAsync(name, ct),
            (name, ct) => reviewerRepo.GetByNameAsync(name, ct),
            (name, ct) => advisorRepo.GetByNameAsync(name, ct),
            (name, ct) => GetGroundingProviderProfileAsync(name, ct),
            cancellationToken);
    }
}
