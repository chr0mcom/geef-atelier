using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Application.Runs;

/// <summary>Application-service contract for submitting, querying, listing, and cancelling pipeline runs.</summary>
public interface IRunService
{
    /// <summary>
    /// Validates and submits a new run, returning its ID. When both <paramref name="crewTemplateName"/>
    /// and <paramref name="customCrew"/> are null, the <c>"klassik"</c> system template is used.
    /// </summary>
    Task<Guid> SubmitRunAsync(
        string briefingText,
        string configJson,
        string? createdByUser = null,
        string? crewTemplateName = null,
        CrewSpec? customCrew = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the run with the given ID, or null if not found.</summary>
    Task<RunEntity?> GetRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Returns the most recent runs, optionally filtered by status, ordered by creation time descending.</summary>
    Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation of the run. Returns true if the cancellation flag was set,
    /// false if the run is already in a terminal state or was already cancelled.
    /// </summary>
    Task<bool> CancelRunAsync(Guid runId, CancellationToken cancellationToken = default);

    /// <summary>Returns the run with all its iterations and findings, or null if not found.</summary>
    Task<RunDetails?> GetRunDetailsAsync(Guid runId, CancellationToken cancellationToken = default);
}
