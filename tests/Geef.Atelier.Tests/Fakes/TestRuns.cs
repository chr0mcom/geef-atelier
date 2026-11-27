using System.Text.Json;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>Creates pending runs of the test loop (<see cref="TestLoop"/>) for tests that need a run without an order.</summary>
internal static class TestRuns
{
    private static readonly JsonSerializerOptions SnapshotJson = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Stores a pending run of <see cref="TestLoop.TemplateName"/> for <paramref name="createdByUser"/> with at most
    /// <paramref name="maxIterations"/> rounds; an orchestrator with <see cref="TestLoopAssembler"/> picks it up. Returns its id.
    /// </summary>
    public static async Task<Guid> CreatePendingAsync(this IServiceProvider services, string briefing, string? createdByUser = null,
        int maxIterations = 3, CancellationToken ct = default)
    {
        var persistence = services.GetRequiredService<IRunPersistenceService>();
        var runId = await persistence.CreateRunAsync(briefing, "{}", createdByUser, TestLoop.TemplateName, null, RunKind.Standard, null, ct);
        await persistence.UpdateSnapshotAsync(runId, JsonSerializer.Serialize(TestLoop.Snapshot(maxIterations), SnapshotJson), ct);
        return runId;
    }
}
