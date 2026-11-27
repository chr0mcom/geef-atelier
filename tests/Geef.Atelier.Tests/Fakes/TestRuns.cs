using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>Creates pending runs for tests that need a run without an order, now that the old submission path is gone.</summary>
internal static class TestRuns
{
    private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Stores a pending run with the snapshot of <paramref name="template"/> (default klassik) for <paramref name="createdByUser"/>;
    /// the orchestrator picks it up like any other pending run. Returns its id.
    /// </summary>
    public static async Task<Guid> CreatePendingAsync(this IServiceProvider services, string briefing, string? createdByUser = null,
        string? template = null, CancellationToken ct = default)
    {
        var resolved = await services.GetRequiredService<ICrewService>().ResolveSnapshotWithWarningsAsync(template, null, ct);
        var persistence = services.GetRequiredService<IRunPersistenceService>();
        var runId = await persistence.CreateRunAsync(briefing, "{}", createdByUser, resolved.Snapshot.TemplateName, null, RunKind.Standard, null, ct);
        await persistence.UpdateSnapshotAsync(runId, JsonSerializer.Serialize(resolved.Snapshot, SnapshotJson), ct);
        return runId;
    }
}
