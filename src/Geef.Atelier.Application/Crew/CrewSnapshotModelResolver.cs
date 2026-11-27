using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Application.Crew;

/// <summary>
/// Replaces always-latest model aliases in a crew snapshot with the concrete model they point at
/// right now.
/// </summary>
/// <remarks>
/// Profiles deliberately keep the alias so every new run picks up the newest model of its family.
/// The snapshot must not: it is the record of what a specific run actually used, and a resumed run
/// would otherwise silently execute on a different model than its parent while the snapshot claims
/// they are the same. Cost rows read their model name from the snapshot, so resolving here also
/// keeps per-model aggregation meaningful across a model generation change.
/// </remarks>
public static class CrewSnapshotModelResolver
{
    /// <summary>
    /// Returns a snapshot whose writer and check models are resolved to concrete ids.
    /// Unknown ids and ids that are not aliases are kept unchanged. Never throws: an unavailable
    /// catalog degrades to the unresolved snapshot rather than failing the run.
    /// </summary>
    public static async Task<CrewSnapshot> ResolveAsync(
        CrewSnapshot snapshot,
        IModelCatalog modelCatalog,
        CancellationToken cancellationToken = default)
    {
        var executor = snapshot.Executor with
        {
            Model = await ResolveAsync(modelCatalog, snapshot.Executor.Provider, snapshot.Executor.Model, cancellationToken),
        };

        var reviewers = new List<ReviewerProfile>(snapshot.Reviewers.Count);
        foreach (var reviewer in snapshot.Reviewers)
        {
            reviewers.Add(reviewer with
            {
                Model = await ResolveAsync(modelCatalog, reviewer.Provider, reviewer.Model, cancellationToken),
            });
        }

        return snapshot with
        {
            Executor = executor,
            Reviewers = reviewers,
        };
    }

    private static async Task<string> ResolveAsync(
        IModelCatalog modelCatalog, string provider, string model, CancellationToken cancellationToken)
    {
        try
        {
            return await modelCatalog.ResolveModelAsync(provider, model, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            return model;
        }
    }
}
