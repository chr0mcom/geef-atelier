using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Core.Persistence.Crew;

/// <summary>
/// Unified read/write access to crew templates. System templates are served from code constants;
/// custom templates are persisted in the database and auto-prefixed with <c>"custom-"</c>.
/// </summary>
public interface ICrewTemplateRepository
{
    /// <summary>Returns all crew templates. When <paramref name="includeSystem"/> is false, only DB-backed custom templates are returned.</summary>
    Task<IReadOnlyList<CrewTemplate>> ListAsync(bool includeSystem = true, CancellationToken cancellationToken = default);

    /// <summary>Returns the crew template with the given name, checking system constants first, then the database. Returns null if not found.</summary>
    Task<CrewTemplate?> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Persists a new custom crew template. Throws if a template with the same name already exists.</summary>
    Task CreateAsync(CrewTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Updates an existing custom crew template identified by <see cref="CrewTemplate.Name"/>.</summary>
    Task UpdateAsync(CrewTemplate template, CancellationToken cancellationToken = default);

    /// <summary>Deletes the custom crew template with the given name. Throws if the template does not exist in the database.</summary>
    Task DeleteAsync(string name, CancellationToken cancellationToken = default);
}
