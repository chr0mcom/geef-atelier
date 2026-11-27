using System.Text;
using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Infrastructure.Composition;

/// <summary>
/// Builds the model-assignment block appended to the crew-composer system prompt (D-066). The composer chooses
/// roles (<c>model_slot</c>) and the crew <c>model_domain</c>; models come from <see cref="ModelMatrix"/>, so no live
/// catalog lookup is needed and the composer can no longer pick outdated or paid routes.
/// </summary>
internal static class ComposerModelCatalog
{
    private static readonly ModelSlot[] ComposerSlots =
    [
        ModelSlot.Executor,
        ModelSlot.FactReviewer,
        ModelSlot.StyleReviewer,
        ModelSlot.DomainReviewer,
        ModelSlot.StrategicAdvisor,
        ModelSlot.CriticalAdvisor,
        ModelSlot.DomainExpertAdvisor,
        ModelSlot.TransformRewrite,
        ModelSlot.TransformCondense,
        ModelSlot.Helper,
    ];

    /// <summary>Returns the Markdown block; <paramref name="groundingProviderTypes"/> lists the registered grounding discriminators.</summary>
    public static string Build(IEnumerable<string> groundingProviderTypes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Model assignment (set `model_domain` + `model_slot` — never provider/model)");
        sb.AppendLine();
        sb.AppendLine("The system assigns every LLM role its model from a researched model matrix. You only choose:");
        sb.AppendLine($"- the crew `model_domain` — exactly one of {string.Join(", ", Enum.GetNames<ModelDomain>().Select(n => $"`{n}`"))}. `Legal` and `Academic` enable paid domain reviewers: use them only for real legal work (claims, contracts, litigation, statutes) or scholarly work (papers, grant proposals); political speeches are `Speech`, spelling and editing `General`;");
        sb.AppendLine("- on every inline executor, reviewer, advisor and Transform finalizer: a `model_slot` from the table below.");
        sb.AppendLine();
        sb.AppendLine("Any `provider`/`model` you send for these roles is ignored. Claude and OpenAI models always run through the subscription CLIs; model plurality (no reviewer or advisor on the executor's model, reviewers preferring another vendor) and spreading same-role reviewers across models are enforced automatically at run start.");
        sb.AppendLine();
        sb.AppendLine("| model_slot | use for | primary model (general domain, info only) |");
        sb.AppendLine("|---|---|---|");
        foreach (var slot in ComposerSlots)
            sb.AppendLine($"| `{slot}` | {slot.Describe()} | `{ModelMatrix.Primary(slot)}` |");
        sb.AppendLine();
        sb.AppendLine("**Choosing reviewer slots:** give each reviewer the slot that matches what it checks — `FactReviewer` for facts, logic, sources and completeness; `StyleReviewer` for language, register, tone and structure; `DomainReviewer` for subject-matter substance, strategy and opposing positions. In `legal` and `academic` crews the DomainReviewer runs on a dedicated low-hallucination model.");
        sb.AppendLine("**Executor rule:** the executor is ALWAYS a NEW inline, task-specialized profile (never reuse) with `model_slot: \"Executor\"` and generous `max_tokens` (>= 32000). Technical/analytical crews (`model_domain: Technical`) automatically get the strongest analysis model.");
        sb.AppendLine("**Transform finalizers:** `TransformRewrite` for voice/tone rewrites, `TransformCondense` for summaries, takeaways and glossaries.");
        sb.AppendLine("**Grounding refinement:** to filter grounding results with an LLM, set `settings: { \"refinementMode\": \"0\" }` on an inline grounding provider — the refiner model is assigned automatically.");
        sb.AppendLine("**Reuse rule:** use `reuse: \"learning-extractor\"` for the output finalizer; reuse existing reviewers/advisors/grounding only when they truly fit — reused profiles keep their own role.");
        sb.AppendLine("**Reuse-only types:** `url-fetch` grounding providers need operator-configured targets — never define them inline, only reuse existing profiles.");

        var types = groundingProviderTypes.OrderBy(t => t, StringComparer.Ordinal).ToList();
        if (types.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Valid grounding provider_type values (ONLY use these for grounding_providers)");
            sb.AppendLine();
            sb.AppendLine(string.Join(", ", types.Select(t => $"`{t}`")));
            sb.AppendLine();
            sb.AppendLine("For web search use `tavily` — there is NO `web` type. " +
                          "`provider_type` is REQUIRED on every inline grounding provider and must be one of the exact values above.");
        }

        return sb.ToString().TrimEnd();
    }
}
