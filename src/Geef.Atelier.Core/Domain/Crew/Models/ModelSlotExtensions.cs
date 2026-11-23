using Geef.Atelier.Core.Domain.Crew.Advisors;

namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>Role semantics of <see cref="ModelSlot"/>: which actor may use which slot, and how slots are described.</summary>
public static class ModelSlotExtensions
{
    private static readonly IReadOnlyList<ModelSlot> ExecutorSlots = [ModelSlot.Executor, ModelSlot.Composer, ModelSlot.Helper];
    private static readonly IReadOnlyList<ModelSlot> ReviewerSlots = [ModelSlot.FactReviewer, ModelSlot.StyleReviewer, ModelSlot.DomainReviewer, ModelSlot.Helper];
    private static readonly IReadOnlyList<ModelSlot> AdvisorSlots = [ModelSlot.StrategicAdvisor, ModelSlot.CriticalAdvisor, ModelSlot.DomainExpertAdvisor];

    /// <summary>Slots a transform finalizer may use.</summary>
    public static IReadOnlyList<ModelSlot> TransformSlots { get; } = [ModelSlot.TransformRewrite, ModelSlot.TransformCondense];

    /// <summary>True for the slots that review drafts (including the helper used by lightweight reviewers).</summary>
    public static bool IsReviewerRole(this ModelSlot slot) =>
        slot is ModelSlot.FactReviewer or ModelSlot.StyleReviewer or ModelSlot.DomainReviewer or ModelSlot.Helper;

    /// <summary>True for advisor slots.</summary>
    public static bool IsAdvisorRole(this ModelSlot slot) =>
        slot is ModelSlot.StrategicAdvisor or ModelSlot.CriticalAdvisor or ModelSlot.DomainExpertAdvisor;

    /// <summary>Slots a profile of <paramref name="kind"/> may carry.</summary>
    public static IReadOnlyList<ModelSlot> AllowedSlots(this ModelActorKind kind) => kind switch
    {
        ModelActorKind.Executor => ExecutorSlots,
        ModelActorKind.Reviewer => ReviewerSlots,
        _ => AdvisorSlots,
    };

    /// <summary>True when <paramref name="slot"/> may be used by a profile of <paramref name="kind"/>.</summary>
    public static bool IsAllowedFor(this ModelSlot slot, ModelActorKind kind) => kind.AllowedSlots().Contains(slot);

    /// <summary>Default slot of an advisor with <paramref name="mode"/>.</summary>
    public static ModelSlot DefaultSlot(this AdvisorMode mode) => mode switch
    {
        AdvisorMode.Critical or AdvisorMode.DevilsAdvocate => ModelSlot.CriticalAdvisor,
        AdvisorMode.DomainExpert => ModelSlot.DomainExpertAdvisor,
        _ => ModelSlot.StrategicAdvisor,
    };

    /// <summary>One-line English description for the UI and the composer prompt.</summary>
    public static string Describe(this ModelSlot slot) => slot switch
    {
        ModelSlot.Executor => "Drafts the text.",
        ModelSlot.FactReviewer => "Reviews facts, logic, sources and completeness.",
        ModelSlot.StyleReviewer => "Reviews language, register, tone and structure.",
        ModelSlot.DomainReviewer => "Reviews subject-matter substance, strategy and counter-positions.",
        ModelSlot.StrategicAdvisor => "Strategic or pre-mortem advice before drafting.",
        ModelSlot.CriticalAdvisor => "Critical advice and devil's advocate challenges.",
        ModelSlot.DomainExpertAdvisor => "Subject-matter expert advice.",
        ModelSlot.GroundingRefiner => "Filters and condenses grounding results.",
        ModelSlot.TransformRewrite => "Transform finalizer that rewrites voice or tone.",
        ModelSlot.TransformCondense => "Transform finalizer that condenses (summary, takeaways, glossary).",
        ModelSlot.Composer => "Composes crews from task descriptions.",
        _ => "Lightweight filtering or classification.",
    };
}
