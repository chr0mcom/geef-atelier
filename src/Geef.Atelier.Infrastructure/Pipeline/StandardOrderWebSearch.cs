using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// An order that runs with the default crew never searches outside (D-079): "Extra careful" from the web and over MCP, and
/// <c>submit_request</c> without a template or own crew. Such a run keeps only the grounding sources that stay inside Atelier
/// (<see cref="LocalProviderTypes"/>, never through the tool path); every web, news, academic, REST or tool source is dropped
/// before the pipeline is built. This holds for every run of such an order, also when it resumes or repeats a run whose stored
/// snapshot still names a web search. A template or own crew that was chosen on purpose keeps its sources: then the order names
/// its template, a crew was composed for it, or the snapshot is not the default one.
/// </summary>
internal static class StandardOrderWebSearch
{
    /// <summary>Grounding provider types that read only what Atelier holds itself and send nothing to a search service.</summary>
    public static readonly IReadOnlySet<string> LocalProviderTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        GroundingProviderTypes.VectorStore,
        GroundingProviderTypes.OrderSources,
        GroundingProviderTypes.LearningRetrieval,
        GroundingProviderTypes.StaticContext,
        GroundingProviderTypes.CrewCatalog,
        GroundingProviderTypes.ToolCatalog,
        GroundingProviderTypes.PackCatalog,
    };

    /// <summary>True when <paramref name="profile"/> runs through a local provider type and not through the tool path.</summary>
    public static bool StaysLocal(this GroundingProviderProfile profile) =>
        LocalProviderTypes.Contains(profile.ProviderType) && !profile.UsesToolPath();

    /// <summary><paramref name="snapshot"/> with only the grounding sources that <see cref="StaysLocal"/>; unchanged when all of them do.</summary>
    public static CrewSnapshot WithoutOutsideSearch(this CrewSnapshot snapshot)
    {
        if (snapshot.GroundingProviders is not { Count: > 0 } providers || providers.All(p => p.StaysLocal()))
            return snapshot;
        return snapshot with { GroundingProviders = providers.Where(p => p.StaysLocal()).ToList() };
    }

    /// <summary>
    /// The snapshot <paramref name="run"/> runs with: <see cref="WithoutOutsideSearch"/> when the snapshot is the default crew, the
    /// order of the run names no template and no crew was composed for it, otherwise unchanged. The order is the one of the run; while
    /// a resumed run is not yet linked, it is the order of the run it continues. Orders taken over from before orders existed name no
    /// template either; their runs are treated like default orders, because it is no longer known whether the template was chosen.
    /// </summary>
    public static async Task<CrewSnapshot> ForRunAsync(this AtelierDbContext db, RunEntity run, CrewSnapshot snapshot, CancellationToken ct)
    {
        if (!string.Equals(snapshot.TemplateName, SystemCrew.KlassikTemplateName, StringComparison.Ordinal))
            return snapshot;
        var local = snapshot.WithoutOutsideSearch();
        if (ReferenceEquals(local, snapshot))
            return snapshot;

        var orderId = run.OrderId ?? await OrderOfAsync(db, run, ct);
        if (orderId is null)
            return snapshot;

        var order = await db.Orders.AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new
            {
                o.CrewTemplateName,
                Composed = db.Runs.Any(r => r.OrderId == o.Id && r.Kind == RunKind.CrewComposition),
            })
            .FirstOrDefaultAsync(ct);
        return order is { Composed: false } && string.IsNullOrWhiteSpace(order.CrewTemplateName) ? local : snapshot;
    }

    private static async Task<Guid?> OrderOfAsync(AtelierDbContext db, RunEntity run, CancellationToken ct)
    {
        var ids = new[] { run.Id, run.ParentRunId, run.ParentCompositionRunId }.OfType<Guid>().ToList();
        var linked = await db.Runs.AsNoTracking()
            .Where(r => ids.Contains(r.Id) && r.OrderId != null)
            .Select(r => new { r.Id, r.OrderId })
            .ToListAsync(ct);
        return (linked.FirstOrDefault(r => r.Id == run.Id) ?? linked.FirstOrDefault())?.OrderId;
    }
}
