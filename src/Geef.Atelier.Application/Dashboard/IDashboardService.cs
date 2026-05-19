using Geef.Atelier.Core.Domain.Dashboard;

namespace Geef.Atelier.Application.Dashboard;

/// <summary>
/// Returns cached dashboard snapshots for a given user/scope combination.
/// Implemented as a singleton with an internal 45-second memory cache.
/// </summary>
public interface IDashboardService
{
    Task<DashboardSnapshot> GetSnapshotAsync(
        string username,
        bool isAdmin,
        DashboardScope scope,
        CancellationToken ct = default);

    /// <summary>Invalidates the cached snapshot for a specific user (both scopes).</summary>
    void InvalidateUser(string username);

    /// <summary>Invalidates all cached snapshots that include all-scope data.</summary>
    void InvalidateAdmin();
}
