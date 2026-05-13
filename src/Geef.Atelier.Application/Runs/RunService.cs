using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Application.Runs;

internal sealed class RunService(
    IRunPersistenceService         persistence,
    IRunRepository                 repository,
    ICrewService                   crewService,
    IAdvisorConsultationRepository consultationRepository) : IRunService
{
    private static readonly JsonSerializerOptions SnapshotJsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <inheritdoc/>
    public async Task<Guid> SubmitRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser = null,
        string? crewTemplateName = null,
        CrewSpec? customCrew = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(briefingText))
            throw new ArgumentException("Briefing text must not be empty.", nameof(briefingText));
        ArgumentNullException.ThrowIfNull(configJson);

        if (!string.IsNullOrEmpty(configJson))
        {
            try { using var _ = JsonDocument.Parse(configJson); }
            catch (JsonException ex)
            { throw new ArgumentException("configJson must be valid JSON.", nameof(configJson), ex); }
        }

        var normalizedConfig = string.IsNullOrEmpty(configJson) ? "{}" : configJson;

        var snapshot = await crewService.ResolveSnapshotAsync(crewTemplateName, customCrew, cancellationToken);
        var snapshotJson = JsonSerializer.Serialize(snapshot, SnapshotJsonOpts);
        // CrewTemplateName is the template name from snapshot (null for inline spec)
        var resolvedTemplateName = snapshot.TemplateName;

        return await persistence.CreateRunAsync(
            briefingText, normalizedConfig, createdByUser,
            resolvedTemplateName, snapshotJson, cancellationToken);
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

        return new RunWithGroundingViewModel(
            details,
            snapshot,
            groundedBrief,
            groundingAdvisors,
            recoveryAdvisors,
            advisorsByIteration);
    }
}
