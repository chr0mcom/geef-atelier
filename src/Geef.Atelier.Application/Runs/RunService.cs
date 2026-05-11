using System.Text.Json;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;

namespace Geef.Atelier.Application.Runs;

internal sealed class RunService(
    IRunPersistenceService persistence,
    IRunRepository         repository) : IRunService
{
    /// <inheritdoc/>
    public Task<Guid> SubmitRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser = null,
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

        // Normalize empty string to "{}" — the DB column is JSONB and requires valid JSON.
        var normalizedConfig = string.IsNullOrEmpty(configJson) ? "{}" : configJson;
        return persistence.CreateRunAsync(briefingText, normalizedConfig, createdByUser, cancellationToken);
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
}
