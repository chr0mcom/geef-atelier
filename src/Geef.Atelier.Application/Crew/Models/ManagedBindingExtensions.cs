using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Application.Crew.Models;

/// <summary>Applies <see cref="ModelBindingPolicy"/> to profile records on every save path (D-066).</summary>
public static class ManagedBindingExtensions
{
    /// <summary>Executor profile with its slot validated and its binding normalized for persistence.</summary>
    public static ExecutorProfile ForSave(this ExecutorProfile profile)
    {
        EnsureAllowed(profile.ModelSlot, ModelActorKind.Executor);
        var b = ModelBindingPolicy.ForSave(new ManagedBinding(profile.Provider, profile.Model, profile.ModelSlot, profile.ModelDomain));
        return profile with { Provider = b.Provider, Model = b.Model, ModelSlot = b.Slot, ModelDomain = b.Domain };
    }

    /// <summary>Reviewer profile with its slot validated and its binding normalized for persistence.</summary>
    public static ReviewerProfile ForSave(this ReviewerProfile profile)
    {
        EnsureAllowed(profile.ModelSlot, ModelActorKind.Reviewer);
        var b = ModelBindingPolicy.ForSave(new ManagedBinding(profile.Provider, profile.Model, profile.ModelSlot, profile.ModelDomain));
        return profile with { Provider = b.Provider, Model = b.Model, ModelSlot = b.Slot, ModelDomain = b.Domain };
    }

    /// <summary>Advisor profile with its slot validated and its binding normalized for persistence.</summary>
    public static AdvisorProfile ForSave(this AdvisorProfile profile)
    {
        EnsureAllowed(profile.ModelSlot, ModelActorKind.Advisor);
        var b = ModelBindingPolicy.ForSave(new ManagedBinding(profile.Provider, profile.Model, profile.ModelSlot, profile.ModelDomain));
        return profile with { Provider = b.Provider, Model = b.Model, ModelSlot = b.Slot, ModelDomain = b.Domain };
    }

    /// <summary>
    /// Grounding profile whose refiner binding obeys the CLI-route rule; a managed refiner (slot marker) keeps a
    /// candidate binding or gets the slot's primary.
    /// </summary>
    public static GroundingProviderProfile ForSave(this GroundingProviderProfile profile)
    {
        var settings = profile.ProviderSettings;
        var provider = settings.GetValueOrDefault(GroundingProviderProfile.KeyRefinementProvider);
        var model = settings.GetValueOrDefault(GroundingProviderProfile.KeyRefinementModel);
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
            return profile;

        var slot = ModelSlotParser.ParseOrNull(settings.GetValueOrDefault(GroundingProviderProfile.KeyModelSlot));
        if (slot is not null && slot != ModelSlot.GroundingRefiner)
            throw new InvalidOperationException($"Model slot '{slot}' is not valid for a grounding refiner.");
        var b = ModelBindingPolicy.ForSave(new ManagedBinding(provider, model, slot, null));
        var updated = new Dictionary<string, string>(settings)
        {
            [GroundingProviderProfile.KeyRefinementProvider] = b.Provider,
            [GroundingProviderProfile.KeyRefinementModel] = b.Model,
        };
        if (b.Slot is null)
            updated.Remove(GroundingProviderProfile.KeyModelSlot);
        return profile with { ProviderSettings = updated };
    }

    /// <summary>
    /// Finalizer profile whose Transform binding obeys the CLI-route rule; a managed Transform (slot marker) keeps a
    /// candidate binding or gets the slot's primary. Other finalizers have no model and are returned unchanged.
    /// </summary>
    public static FinalizerProfile ForSave(this FinalizerProfile profile)
    {
        if (profile.FinalizerType != FinalizerType.Transform)
            return profile;

        var settings = profile.Settings;
        var slot = ModelSlotParser.ParseOrNull(settings.GetValueOrDefault(TransformSettings.KeyModelSlot));
        if (slot is { } s && !ModelSlotExtensions.TransformSlots.Contains(s))
            throw new InvalidOperationException($"Model slot '{s}' is not valid for a Transform finalizer.");

        var provider = settings.GetValueOrDefault(TransformSettings.KeyProvider);
        var model = settings.GetValueOrDefault(TransformSettings.KeyModel);
        if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(model))
            return profile;

        var b = ModelBindingPolicy.ForSave(new ManagedBinding(provider, model, slot, null));
        var updated = new Dictionary<string, string>(settings)
        {
            [TransformSettings.KeyProvider] = b.Provider,
            [TransformSettings.KeyModel] = b.Model,
        };
        if (b.Slot is null)
            updated.Remove(TransformSettings.KeyModelSlot);
        return profile with { Settings = updated };
    }

    private static void EnsureAllowed(ModelSlot? slot, ModelActorKind kind)
    {
        if (slot is { } s && !s.IsAllowedFor(kind))
            throw new InvalidOperationException($"Model slot '{s}' is not valid for a {kind.ToString().ToLowerInvariant()} profile.");
    }
}
