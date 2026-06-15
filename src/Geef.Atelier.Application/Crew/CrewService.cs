using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.Crew.Specialization;
using Geef.Atelier.Core.Persistence.Crew;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Application.Crew;

internal sealed class CrewService(
    IReviewerProfileRepository reviewerRepo,
    IExecutorProfileRepository executorRepo,
    IAdvisorProfileRepository advisorRepo,
    IGroundingProviderProfileRepository groundingRepo,
    IFinalizerProfileRepository finalizerRepo,
    ICrewTemplateRepository templateRepo,
    ISpecializationPackRepository packRepo,
    ILogger<CrewService> logger) : ICrewService
{
    private const string ReadOnlyMessage = "System profile is read-only — copy it as a custom variant.";
    private const string ReadOnlyAdvisorMessage = "System advisor profile is read-only — copy it as a custom variant.";
    private const string ReadOnlyGroundingMessage = "System grounding-provider profile is read-only — copy it as a custom variant.";
    private const string ReadOnlyFinalizerMessage = "System finalizer profile is read-only — copy it as a custom variant.";
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

    public Task<string> RenameCustomReviewerProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        => RenameAsync(
            oldName, newName, ReadOnlyMessage,
            SystemCrew.IsSystemName,
            n => reviewerRepo.GetByNameAsync(n, cancellationToken),
            (o, n) => reviewerRepo.RenameAsync(o, n, cancellationToken));

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

    public Task<string> RenameCustomExecutorProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        => RenameAsync(
            oldName, newName, ReadOnlyMessage,
            SystemCrew.IsSystemName,
            n => executorRepo.GetByNameAsync(n, cancellationToken),
            (o, n) => executorRepo.RenameAsync(o, n, cancellationToken));

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

    public Task<string> RenameCustomAdvisorProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        => RenameAsync(
            oldName, newName, ReadOnlyAdvisorMessage,
            SystemCrew.IsSystemAdvisorName,
            n => advisorRepo.GetByNameAsync(n, cancellationToken),
            (o, n) => advisorRepo.RenameAsync(o, n, cancellationToken));

    // --- Grounding-provider profiles ---

    public async Task<IReadOnlyList<GroundingProviderProfile>> ListGroundingProviderProfilesAsync(
        bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        var dbProfiles = await groundingRepo.ListAsync(cancellationToken);
        var customOnly = dbProfiles.Where(p => !p.IsSystem).ToList();
        if (!includeSystem)
            return customOnly;

        // Warn about DB system profiles not tracked by SystemCrew — these would otherwise be silently dropped.
        foreach (var orphan in dbProfiles.Where(p => p.IsSystem && !SystemCrew.GroundingProviderProfiles.ContainsKey(p.Name)))
            logger.LogWarning(
                "Grounding-provider profile '{Name}' (type '{Type}') is marked IsSystem in the database but is not registered in SystemCrew.GroundingProviderProfiles. It will not appear in listings. Add it as a code constant to make it visible.",
                orphan.Name, orphan.ProviderType);

        var system = SystemCrew.GroundingProviderProfiles.Values.ToList();
        return [.. system, .. customOnly];
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

    public Task<string> RenameCustomGroundingProviderProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        => RenameAsync(
            oldName, newName, ReadOnlyGroundingMessage,
            SystemCrew.IsSystemGroundingProviderName,
            n => GetGroundingProviderProfileAsync(n, cancellationToken),
            (o, n) => groundingRepo.RenameAsync(o, n, cancellationToken));

    // --- Finalizer profiles ---

    public async Task<IReadOnlyList<FinalizerProfile>> ListFinalizerProfilesAsync(
        bool includeSystem = true, CancellationToken cancellationToken = default)
    {
        var dbRows = await finalizerRepo.ListAsync(cancellationToken);
        if (!includeSystem)
            return dbRows.Where(p => !p.IsSystem).ToList();
        var system = SystemCrew.FinalizerProfiles.Values.ToList();
        // Exclude DB rows already covered by code-defined system profiles to avoid duplicates.
        var dbOnly = dbRows.Where(p => !SystemCrew.FinalizerProfiles.ContainsKey(p.Name)).ToList();
        return [.. system, .. dbOnly];
    }

    public async Task<FinalizerProfile?> GetFinalizerProfileAsync(
        string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.FinalizerProfiles.TryGetValue(name, out var system))
            return system;
        return await finalizerRepo.GetByNameAsync(name, cancellationToken);
    }

    public async Task<FinalizerProfile> CreateCustomFinalizerProfileAsync(
        FinalizerProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemFinalizerName(profile.Name))
            throw new InvalidOperationException(ReadOnlyFinalizerMessage);
        var baseName = SystemCrew.EnsureCustomPrefix(profile.Name);
        var uniqueName = await UniqueNameAsync(baseName, n => finalizerRepo.GetByNameAsync(n, cancellationToken));
        var normalized = profile with { Name = uniqueName, IsSystem = false, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        await finalizerRepo.CreateAsync(normalized, cancellationToken);
        return normalized;
    }

    public async Task<FinalizerProfile> UpdateCustomFinalizerProfileAsync(
        FinalizerProfile profile, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemFinalizerName(profile.Name))
            throw new InvalidOperationException(ReadOnlyFinalizerMessage);
        var updated = profile with { UpdatedAt = DateTimeOffset.UtcNow };
        await finalizerRepo.UpdateAsync(updated, cancellationToken);
        return updated;
    }

    public Task DeleteCustomFinalizerProfileAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemFinalizerName(name))
            throw new InvalidOperationException(ReadOnlyFinalizerMessage);
        return finalizerRepo.DeleteAsync(name, cancellationToken);
    }

    public Task<string> RenameCustomFinalizerProfileAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        => RenameAsync(
            oldName, newName, ReadOnlyFinalizerMessage,
            SystemCrew.IsSystemFinalizerName,
            n => GetFinalizerProfileAsync(n, cancellationToken),
            (o, n) => finalizerRepo.RenameAsync(o, n, cancellationToken));

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
        await ValidatePackCoherenceAsync(normalized, cancellationToken);
        await templateRepo.CreateAsync(normalized, cancellationToken);
        return normalized;
    }

    public async Task<CrewTemplate> UpdateCustomCrewTemplateAsync(CrewTemplate template, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemName(template.Name))
            throw new InvalidOperationException(ReadOnlyTemplateMessage);
        await ValidatePackCoherenceAsync(template, cancellationToken);
        await templateRepo.UpdateAsync(template, cancellationToken);
        return template;
    }

    /// <summary>
    /// Crew-coherence check: a template may only bind packs that exist, are type-compatible with the
    /// actor, and — for TaskBound packs — are owned by this very crew. Foreign TaskBound packs are a
    /// hard block (they would leak another crew's task-specific specialization).
    /// </summary>
    private async Task ValidatePackCoherenceAsync(CrewTemplate template, CancellationToken ct)
    {
        foreach (var (key, packNames) in template.ActorPackBindings)
        {
            var actorType = ParseActorType(key);
            foreach (var packName in packNames)
            {
                var pack = await packRepo.GetByNameAsync(packName, ct);
                if (pack is null)
                    throw new InvalidOperationException($"Pack '{packName}' (bound at '{key}') was not found.");

                if (!pack.ApplicableActorTypes.AppliesTo(actorType))
                    throw new InvalidOperationException(
                        $"Pack '{packName}' is not applicable to actor type '{actorType}' (binding '{key}').");

                if (pack.Scope == PackScope.TaskBound &&
                    !string.Equals(pack.OwningCrewId, template.Name, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Pack '{packName}' is TaskBound to another crew ('{pack.OwningCrewId}') and cannot be bound in template '{template.Name}'.");
            }
        }
    }

    private static PackActorType ParseActorType(string bindingKey)
    {
        var prefix = bindingKey.Split(':', 2)[0];
        return prefix switch
        {
            ActorTypeKeys.Executor => PackActorType.Executor,
            ActorTypeKeys.Reviewer => PackActorType.Reviewer,
            ActorTypeKeys.Advisor  => PackActorType.Advisor,
            _                      => PackActorType.Any
        };
    }

    public Task<SpecializationPack?> GetSpecializationPackAsync(string name, CancellationToken cancellationToken = default)
        => packRepo.GetByNameAsync(name, cancellationToken);

    public async Task<SpecializationPack> CreateCustomSpecializationPackAsync(SpecializationPack pack, CancellationToken cancellationToken = default)
    {
        var baseName = SystemCrew.EnsureCustomPrefix(pack.Name);
        var uniqueName = await UniqueNameAsync(baseName, n => packRepo.GetByNameAsync(n, cancellationToken));
        var now = DateTimeOffset.UtcNow;
        var normalized = pack with
        {
            Name = uniqueName,
            IsSystem = false,
            CreatedAt = pack.CreatedAt ?? now,
            UpdatedAt = now
        };
        await packRepo.UpsertAsync(normalized, cancellationToken);
        return normalized;
    }

    public async Task DeleteCustomCrewTemplateAsync(string name, CancellationToken cancellationToken = default)
    {
        if (SystemCrew.IsSystemName(name))
            throw new InvalidOperationException(ReadOnlyTemplateMessage);

        // Cascade: TaskBound packs are owned by this crew and have no purpose without it.
        var removed = await packRepo.DeleteByOwningCrewAsync(name, cancellationToken);
        if (removed > 0)
            logger.LogInformation("Deleted {Count} TaskBound pack(s) owned by crew template '{Template}'.", removed, name);

        await templateRepo.DeleteAsync(name, cancellationToken);
    }

    public Task<string> RenameCustomCrewTemplateAsync(string oldName, string newName, CancellationToken cancellationToken = default)
        => RenameAsync(
            oldName, newName, ReadOnlyTemplateMessage,
            SystemCrew.IsSystemName,
            n => templateRepo.GetByNameAsync(n, cancellationToken),
            (o, n) => templateRepo.RenameAsync(o, n, cancellationToken));

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

    /// <summary>
    /// Shared rename pipeline: rejects system entries, enforces the <c>custom-</c> prefix on the
    /// target, fails on an already-used name (an explicit rename should not silently de-duplicate),
    /// then delegates the atomic rename-and-cascade to the repository. Returns the final name.
    /// </summary>
    private static async Task<string> RenameAsync<T>(
        string oldName,
        string newName,
        string readOnlyMessage,
        Func<string, bool> isSystem,
        Func<string, Task<T?>> lookup,
        Func<string, string, Task> repoRename) where T : class
    {
        if (isSystem(oldName))
            throw new InvalidOperationException(readOnlyMessage);
        if (string.IsNullOrWhiteSpace(newName))
            throw new InvalidOperationException("A name is required.");

        var finalName = SystemCrew.EnsureCustomPrefix(newName.Trim());
        if (finalName == oldName)
            return oldName;
        if (await lookup(finalName) is not null)
            throw new InvalidOperationException($"The name '{finalName}' is already in use.");

        await repoRename(oldName, finalName);
        return finalName;
    }

    // --- Snapshot ---

    public async Task<CrewSnapshot> ResolveSnapshotAsync(
        string? crewTemplateName, CrewSpec? customCrew, CancellationToken cancellationToken = default)
    {
        if (customCrew is not null)
        {
            var customSnapshot = await CrewSnapshotBuilder.BuildAsync(
                customCrew,
                (name, ct) => executorRepo.GetByNameAsync(name, ct),
                (name, ct) => reviewerRepo.GetByNameAsync(name, ct),
                (name, ct) => advisorRepo.GetByNameAsync(name, ct),
                (name, ct) => GetGroundingProviderProfileAsync(name, ct),
                (name, ct) => GetFinalizerProfileAsync(name, ct),
                cancellationToken);

            return await ApplyPacksAsync(customSnapshot, customCrew.ActorPackBindings, cancellationToken);
        }

        var templateName = crewTemplateName ?? SystemCrew.KlassikTemplateName;
        var template = await templateRepo.GetByNameAsync(templateName, cancellationToken)
            ?? throw new InvalidOperationException($"Crew template '{templateName}' not found.");

        var snapshot = await CrewSnapshotBuilder.BuildAsync(
            template,
            (name, ct) => executorRepo.GetByNameAsync(name, ct),
            (name, ct) => reviewerRepo.GetByNameAsync(name, ct),
            (name, ct) => advisorRepo.GetByNameAsync(name, ct),
            (name, ct) => GetGroundingProviderProfileAsync(name, ct),
            (name, ct) => GetFinalizerProfileAsync(name, ct),
            cancellationToken);

        return await ApplyPacksAsync(snapshot, template.ActorPackBindings, cancellationToken);
    }

    private async Task<CrewSnapshot> ApplyPacksAsync(
        CrewSnapshot snapshot,
        IReadOnlyDictionary<string, IReadOnlyList<string>> bindings,
        CancellationToken cancellationToken)
    {
        var composed = await CrewSnapshotBuilder.ApplyPacksAsync(
            snapshot, bindings, (name, ct) => packRepo.GetByNameAsync(name, ct), logger, cancellationToken);

        // Best-effort: mark composed packs as recently used (drives the auto-GC freshness window).
        if (composed.PromptCompositions is { Count: > 0 } comps)
        {
            var usedNames = comps.SelectMany(c => c.Packs.Select(p => p.Name)).Distinct().ToList();
            try { await packRepo.TouchLastUsedAsync(usedNames, DateTimeOffset.UtcNow, cancellationToken); }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to update LastUsedAt for {Count} packs.", usedNames.Count); }
        }

        return composed;
    }
}
