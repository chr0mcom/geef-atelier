using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
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
    /// Returns a snapshot whose executor, reviewer and advisor models are resolved to concrete ids.
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

        var advisors = new List<AdvisorProfile>(snapshot.Advisors.Count);
        foreach (var advisor in snapshot.Advisors)
        {
            advisors.Add(advisor with
            {
                Model = await ResolveAsync(modelCatalog, advisor.Provider, advisor.Model, cancellationToken),
            });
        }

        return snapshot with
        {
            Executor = executor,
            Reviewers = reviewers,
            Advisors = advisors,
            GroundingProviders = await ResolveGroundingProvidersAsync(snapshot, modelCatalog, cancellationToken),
            Finalizers = await ResolveFinalizersAsync(snapshot, modelCatalog, cancellationToken),
        };
    }

    /// <summary>
    /// Resolves the optional refiner binding a grounding provider keeps in its settings. The picker
    /// offers the same aliases here as everywhere else, so leaving these out would put an
    /// unresolved alias into the snapshot through the one actor class nobody looks at.
    /// </summary>
    private static async Task<IReadOnlyList<GroundingProviderProfile>?> ResolveGroundingProvidersAsync(
        CrewSnapshot snapshot, IModelCatalog modelCatalog, CancellationToken cancellationToken)
    {
        if (snapshot.GroundingProviders is not { Count: > 0 } providers)
            return snapshot.GroundingProviders;

        var resolved = new List<GroundingProviderProfile>(providers.Count);
        foreach (var profile in providers)
        {
            resolved.Add(await WithResolvedSettingAsync(
                profile,
                GroundingProviderProfile.KeyRefinementProvider,
                GroundingProviderProfile.KeyRefinementModel,
                p => p.ProviderSettings,
                (p, settings) => p with { ProviderSettings = settings },
                modelCatalog,
                cancellationToken));
        }

        return resolved;
    }

    /// <summary>Resolves the model an LLM-backed transform finalizer keeps in its settings.</summary>
    private static async Task<IReadOnlyList<FinalizerProfile>?> ResolveFinalizersAsync(
        CrewSnapshot snapshot, IModelCatalog modelCatalog, CancellationToken cancellationToken)
    {
        if (snapshot.Finalizers is not { Count: > 0 } finalizers)
            return snapshot.Finalizers;

        var resolved = new List<FinalizerProfile>(finalizers.Count);
        foreach (var profile in finalizers)
        {
            resolved.Add(await WithResolvedSettingAsync(
                profile,
                TransformSettings.KeyProvider,
                TransformSettings.KeyModel,
                p => p.Settings,
                (p, settings) => p with { Settings = settings },
                modelCatalog,
                cancellationToken));
        }

        return resolved;
    }

    private static async Task<T> WithResolvedSettingAsync<T>(
        T profile,
        string providerKey,
        string modelKey,
        Func<T, Dictionary<string, string>> readSettings,
        Func<T, Dictionary<string, string>, T> writeSettings,
        IModelCatalog modelCatalog,
        CancellationToken cancellationToken)
    {
        var settings = readSettings(profile);

        if (!settings.TryGetValue(providerKey, out var provider) || string.IsNullOrWhiteSpace(provider)
            || !settings.TryGetValue(modelKey, out var model) || string.IsNullOrWhiteSpace(model))
        {
            return profile;
        }

        var resolved = await ResolveAsync(modelCatalog, provider, model, cancellationToken);
        if (string.Equals(resolved, model, StringComparison.Ordinal))
            return profile;

        return writeSettings(profile, new Dictionary<string, string>(settings) { [modelKey] = resolved });
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
