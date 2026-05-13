using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Application.Crew;

internal sealed class CrewService(
    IReviewerProfileRepository reviewerRepo,
    IExecutorProfileRepository executorRepo,
    ICrewTemplateRepository templateRepo) : ICrewService
{
    private const string ReadOnlyMessage = "System profile is read-only — copy it as a custom variant.";
    private const string ReadOnlyTemplateMessage = "System template is read-only — copy it as a custom variant.";

    // --- Reviewer profiles ---

    public Task<IReadOnlyList<ReviewerProfile>> ListReviewerProfilesAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
        => reviewerRepo.ListAsync(includeSystem, cancellationToken);

    public Task<ReviewerProfile?> GetReviewerProfileAsync(string name, CancellationToken cancellationToken = default)
        => reviewerRepo.GetByNameAsync(name, cancellationToken);

    public async Task<ReviewerProfile> CreateCustomReviewerProfileAsync(ReviewerProfile profile, CancellationToken cancellationToken = default)
    {
        var normalized = profile with { Name = SystemCrew.EnsureCustomPrefix(profile.Name), IsSystem = false };
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
        var normalized = profile with { Name = SystemCrew.EnsureCustomPrefix(profile.Name), IsSystem = false };
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

    // --- Crew templates ---

    public Task<IReadOnlyList<CrewTemplate>> ListCrewTemplatesAsync(bool includeSystem = true, CancellationToken cancellationToken = default)
        => templateRepo.ListAsync(includeSystem, cancellationToken);

    public Task<CrewTemplate?> GetCrewTemplateAsync(string name, CancellationToken cancellationToken = default)
        => templateRepo.GetByNameAsync(name, cancellationToken);

    public async Task<CrewTemplate> CreateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default)
    {
        var normalized = template with { Name = SystemCrew.EnsureCustomPrefix(template.Name), IsSystem = false };
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

    // --- Snapshot ---

    public async Task<CrewSnapshot> ResolveSnapshotAsync(
        string? crewTemplateName, CrewSpec? customCrew, CancellationToken cancellationToken = default)
    {
        if (customCrew is not null)
            return await CrewSnapshotBuilder.BuildAsync(
                customCrew,
                (name, ct) => executorRepo.GetByNameAsync(name, ct),
                (name, ct) => reviewerRepo.GetByNameAsync(name, ct),
                cancellationToken);

        var templateName = crewTemplateName ?? SystemCrew.KlassikTemplateName;
        var template = await templateRepo.GetByNameAsync(templateName, cancellationToken)
            ?? throw new InvalidOperationException($"Crew template '{templateName}' not found.");

        return await CrewSnapshotBuilder.BuildAsync(
            template,
            (name, ct) => executorRepo.GetByNameAsync(name, ct),
            (name, ct) => reviewerRepo.GetByNameAsync(name, ct),
            cancellationToken);
    }
}
