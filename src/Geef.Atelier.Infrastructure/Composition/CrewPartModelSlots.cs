using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Crew.Composition;
using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>Kind of composer part, for model-slot defaults and validation.</summary>
internal enum CrewPartKind
{
    Executor,
    Reviewer,
    Advisor,
    TransformFinalizer,
}

/// <summary>Shared model-slot rules for composer parts (D-066), used by the validator and the materializer alike.</summary>
internal static class CrewPartModelSlots
{
    /// <summary>Slot used when the composer omitted <c>model_slot</c> on an inline part.</summary>
    public static ModelSlot DefaultSlot(this CrewPartSpec part, CrewPartKind kind) => kind switch
    {
        CrewPartKind.Executor => ModelSlot.Executor,
        CrewPartKind.Reviewer => ModelSlot.DomainReviewer,
        CrewPartKind.Advisor => part.ParsedAdvisorMode().DefaultSlot(),
        _ => ModelSlot.TransformCondense,
    };

    /// <summary>The slot the part will be materialized with: the parsed <c>model_slot</c> or the kind's default.</summary>
    public static ModelSlot EffectiveSlot(this CrewPartSpec part, CrewPartKind kind) =>
        ModelSlotParser.TryParse(part.ModelSlot, out var slot) && slot.IsAllowedFor(kind) ? slot : part.DefaultSlot(kind);

    /// <summary>True when <paramref name="slot"/> may be used by a part of <paramref name="kind"/>.</summary>
    public static bool IsAllowedFor(this ModelSlot slot, CrewPartKind kind) => kind switch
    {
        CrewPartKind.Executor => slot.IsAllowedFor(ModelActorKind.Executor),
        CrewPartKind.Reviewer => slot.IsAllowedFor(ModelActorKind.Reviewer),
        CrewPartKind.Advisor => slot.IsAllowedFor(ModelActorKind.Advisor),
        _ => ModelSlotExtensions.TransformSlots.Contains(slot),
    };

    /// <summary>Slots a part of <paramref name="kind"/> may use, for validation messages.</summary>
    public static IReadOnlyList<ModelSlot> AllowedSlots(this CrewPartKind kind) => kind switch
    {
        CrewPartKind.Executor => ModelActorKind.Executor.AllowedSlots(),
        CrewPartKind.Reviewer => ModelActorKind.Reviewer.AllowedSlots(),
        CrewPartKind.Advisor => ModelActorKind.Advisor.AllowedSlots(),
        _ => ModelSlotExtensions.TransformSlots,
    };

    /// <summary>
    /// Slot and display binding for an inline LLM part: the composer's <c>model_slot</c> (or the kind's default) and
    /// the matrix primary for the crew domain. A differing composer binding is overridden and reported in <paramref name="warnings"/>.
    /// </summary>
    public static (ModelSlot Slot, ModelCandidate Binding) AssignMatrixBinding(
        this CrewPartSpec part, CrewPartKind kind, ModelDomain crewDomain, List<string> warnings, string path)
    {
        var slot = part.EffectiveSlot(kind);
        var binding = ModelMatrix.Primary(slot, crewDomain);
        if (!string.IsNullOrWhiteSpace(part.Provider) && !string.IsNullOrWhiteSpace(part.Model)
            && !binding.SameBindingAs(part.Provider!, part.Model!))
        {
            warnings.Add($"[{path}] composer binding '{part.Provider}/{part.Model}' overridden by the model matrix (slot {slot}) → '{binding}'.");
        }

        return (slot, binding);
    }

    /// <summary>The part's composer advisor mode; unknown values default to <see cref="AdvisorMode.Strategic"/>.</summary>
    public static AdvisorMode ParsedAdvisorMode(this CrewPartSpec part) =>
        part.AdvisorMode?.ToLowerInvariant() switch
        {
            "critical" => AdvisorMode.Critical,
            "devilsadvocate" => AdvisorMode.DevilsAdvocate,
            "domainexpert" => AdvisorMode.DomainExpert,
            _ => AdvisorMode.Strategic,
        };
}
