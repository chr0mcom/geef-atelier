namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>
/// Pure save rule for profile bindings (D-066). The caller states its intent explicitly through the slot:
/// a slot means "managed by the model matrix", no slot means "manual". Nothing is inferred from the model.
/// </summary>
public static class ModelBindingPolicy
{
    /// <summary>
    /// Returns the binding to persist for <paramref name="incoming"/>.
    /// <list type="bullet">
    ///   <item>No slot: manual; binding CLI-normalized, domain cleared.</item>
    ///   <item>Slot: managed; the stored binding is only a display value and stays when it is a candidate of the
    ///   slot, otherwise it becomes the slot's primary. The run-time model is assigned at snapshot-build time.</item>
    /// </list>
    /// </summary>
    public static ManagedBinding ForSave(ManagedBinding incoming)
    {
        if (incoming.Slot is not { } slot)
        {
            var normalized = CliRoute.Normalize(incoming.Provider, incoming.Model);
            return new ManagedBinding(normalized.Provider, normalized.Model, Slot: null, Domain: null);
        }

        var candidates = ModelMatrix.CandidatesFor(slot, incoming.Domain ?? ModelDomain.General);
        if (candidates.Any(c => c.SameBindingAs(incoming.Provider, incoming.Model)))
            return incoming;

        var primary = candidates[0];
        return incoming with { Provider = primary.Provider, Model = primary.Model };
    }
}
