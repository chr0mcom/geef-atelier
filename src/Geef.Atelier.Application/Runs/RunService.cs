using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Application.Runs;

internal sealed class RunService(
    IRunPersistenceService                persistence,
    IRunRepository                        repository,
    ICrewService                          crewService,
    IAdvisorConsultationRepository        consultationRepository,
    IKnowledgeService?                    knowledgeService = null,
    IGroundingConsultationRepository?     groundingConsultationRepository = null) : IRunService
{
    private static readonly JsonSerializerOptions SnapshotJsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc/>
    public async Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.BriefingText))
            throw new ArgumentException("Briefing text must not be empty.", nameof(request));
        ArgumentNullException.ThrowIfNull(request.ConfigJson);

        if (!string.IsNullOrEmpty(request.ConfigJson))
        {
            try { using var _ = JsonDocument.Parse(request.ConfigJson); }
            catch (JsonException ex)
            { throw new ArgumentException("configJson must be valid JSON.", nameof(request), ex); }
        }

        var normalizedConfig = string.IsNullOrEmpty(request.ConfigJson) ? "{}" : request.ConfigJson;

        var snapshot = await crewService.ResolveSnapshotAsync(request.CrewTemplateName, request.CustomCrew, cancellationToken);
        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOpts);
        // CrewTemplateName is the template name from snapshot (null for inline spec)
        var resolvedTemplateName = snapshot.TemplateName;

        var runId = await persistence.CreateRunAsync(
            request.BriefingText, normalizedConfig, request.CreatedByUser,
            resolvedTemplateName, snapshotJson, cancellationToken);

        // Upload run-local attachments and extend the snapshot with RunAttachmentsProfile.
        if (request.Attachments is { Count: > 0 } attachments && knowledgeService is not null)
        {
            foreach (var attachment in attachments)
            {
                await knowledgeService.UploadRunAttachmentAsync(
                    runId,
                    attachment.Filename,
                    new MemoryStream(attachment.Content),
                    attachment.Filename,
                    attachment.ContentType,
                    cancellationToken);
            }

            var extendedProviders = new List<GroundingProviderProfile>
                { SystemCrew.RunAttachmentsProfile };
            if (snapshot.GroundingProviders is { Count: > 0 } existing)
                extendedProviders.AddRange(existing);

            var extendedSnapshot = snapshot with { GroundingProviders = extendedProviders };
            var extendedSnapshotJson = JsonSerializer.Serialize(extendedSnapshot, SnapshotJsonOpts);
            await persistence.UpdateSnapshotAsync(runId, extendedSnapshotJson, cancellationToken);
        }

        return runId;
    }

    /// <inheritdoc/>
    public Task<RunEntity?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => repository.GetByIdAsync(runId, cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, CancellationToken cancellationToken = default)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        return repository.ListAsync(limit, statusFilter, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default)
        => repository.RequestCancellationAsync(runId, cancellationToken);

    /// <inheritdoc/>
    public Task<RunDetails?> GetRunDetailsAsync(Guid runId, CancellationToken cancellationToken = default)
        => repository.GetDetailsAsync(runId, cancellationToken);

    /// <inheritdoc/>
    public async Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        var details = await repository.GetDetailsAsync(runId, cancellationToken);
        if (details is null)
            return null;

        var snapshot = details.Run.CrewSnapshot is { } s ? CrewSnapshot.Deserialize(s) : null;
        var groundedBrief = details.Run.BriefingText;

        var consultations = await consultationRepository.GetByRunIdAsync(runId, cancellationToken);

        // Build a name→trigger lookup from the snapshot's advisor profiles (null-safe).
        var triggerDict = snapshot is not null
            ? snapshot.Advisors.ToDictionary(a => a.Name, a => a.Trigger, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, AdvisorTrigger>(StringComparer.OrdinalIgnoreCase);

        var recoveryAdvisors = consultations
            .Where(c => c.IterationNumber == -1)
            .ToList();

        var nonRecovery = consultations
            .Where(c => c.IterationNumber != -1)
            .ToList();

        var groundingAdvisors = nonRecovery
            .Where(c => triggerDict.TryGetValue(c.AdvisorProfileName, out var t)
                        && t == AdvisorTrigger.BeforeFirstExecution)
            .ToList();

        // This covers BeforeEveryExecution as well as fallback for profiles not found in the snapshot.
        var iterationSet = new HashSet<Guid>(groundingAdvisors.Select(c => c.Id));
        var advisorsByIteration = nonRecovery
            .Where(c => !iterationSet.Contains(c.Id))
            .ToLookup(c => c.IterationNumber);

        IReadOnlyList<GroundingConsultation> groundingConsultations = groundingConsultationRepository is not null
            ? await groundingConsultationRepository.GetByRunIdAsync(runId, cancellationToken)
            : [];

        return new RunWithGroundingViewModel(
            details,
            snapshot,
            groundedBrief,
            groundingAdvisors,
            recoveryAdvisors,
            advisorsByIteration,
            groundingConsultations);
    }
}
