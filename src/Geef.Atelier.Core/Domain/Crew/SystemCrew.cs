using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Read-only catalogue of system-defined crew profiles and templates. These constants are
/// versioned with the Atelier source and ship as immutable defaults; user-editable variants
/// live in the database under the <c>"custom-"</c> name prefix.
/// </summary>
/// <remarks>
/// <para>Model defaults follow the Atelier model-pluralism convention (Vision doc, Leitstern 3 +
/// CLAUDE.md "Reviewer-Modell außerhalb der Anthropic-Familie"): the executor stays on Anthropic
/// (continuity with PS-2), reviewers use external models for genuine outside perspective.</para>
/// <para>If a chosen reviewer model is unavailable on OpenRouter or regresses against the
/// Hadwiger-Nelson replay (PS-5 AC 10), fall back to <c>anthropic/claude-opus-4.7</c> for all
/// reviewers and document the change in the PS-5 report.</para>
/// </remarks>
public static class SystemCrew
{
    /// <summary>Prefix automatically applied to user-created profile/template names to prevent collisions with system entries.</summary>
    public const string CustomPrefix = "custom-";

    /// <summary>Name of the only system template shipped with PS-5.</summary>
    public const string KlassikTemplateName = "klassik";

    /// <summary>Default executor profile — drafting LLM with Atelier's standard prompt.</summary>
    public static readonly ExecutorProfile DefaultExecutorProfile = new(
        Name: "default-executor",
        DisplayName: "Default Executor",
        Description: "Standard Atelier drafting executor: clear, concise, briefing-bound prose; revises iterations against reviewer findings.",
        SystemPrompt: SystemPrompts.Executor,
        Provider: "openrouter",
        Model: "anthropic/claude-opus-4.7",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Briefing-fidelity reviewer — checks the draft against briefing requirements (PS-2 severity calibration).</summary>
    public static readonly ReviewerProfile BriefingFidelityProfile = new(
        Name: "briefing-fidelity",
        DisplayName: "Briefing Fidelity",
        Description: "Verifies that the draft fully addresses every briefing requirement. Outside-model perspective for genuine independence from the executor.",
        SystemPrompt: SystemPrompts.BriefingFidelity,
        Provider: "openrouter",
        Model: "google/gemini-2.5-flash",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>Clarity reviewer — checks argumentation, structure, and style (PS-2 severity calibration).</summary>
    public static readonly ReviewerProfile ClarityProfile = new(
        Name: "clarity",
        DisplayName: "Clarity",
        Description: "Audits clarity, argumentation, structure, and style. Outside-model perspective; complements the briefing-fidelity reviewer with a different model family.",
        SystemPrompt: SystemPrompts.Clarity,
        Provider: "openrouter",
        Model: "openai/gpt-5.5-mini",
        MaxTokens: null,
        IsSystem: true);

    /// <summary>The only system template in PS-5: the Klassik crew that reproduces the PS-2 hardcoded behaviour.</summary>
    public static readonly CrewTemplate KlassikTemplate = new(
        Name: KlassikTemplateName,
        DisplayName: "Klassik",
        Description: "The default Atelier crew: one drafting executor plus briefing-fidelity and clarity reviewers running in parallel. Reproduces the pre-PS-5 hardcoded pipeline.",
        ExecutorProfileName: DefaultExecutorProfile.Name,
        ReviewerProfileNames: new[] { BriefingFidelityProfile.Name, ClarityProfile.Name },
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: null,
        AdvisorProfileNames: Array.Empty<string>(),
        IsSystem: true);

    /// <summary>All system reviewer profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ReviewerProfile> ReviewerProfiles =
        new Dictionary<string, ReviewerProfile>
        {
            [BriefingFidelityProfile.Name] = BriefingFidelityProfile,
            [ClarityProfile.Name] = ClarityProfile,
        };

    /// <summary>All system executor profiles, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ExecutorProfile> ExecutorProfiles =
        new Dictionary<string, ExecutorProfile>
        {
            [DefaultExecutorProfile.Name] = DefaultExecutorProfile,
        };

    /// <summary>All system crew templates, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, CrewTemplate> CrewTemplates =
        new Dictionary<string, CrewTemplate>
        {
            [KlassikTemplate.Name] = KlassikTemplate,
        };

    /// <summary>True when the supplied name matches a system profile or template (any kind).</summary>
    public static bool IsSystemName(string name) =>
        ReviewerProfiles.ContainsKey(name)
        || ExecutorProfiles.ContainsKey(name)
        || CrewTemplates.ContainsKey(name);

    /// <summary>Ensures the supplied name carries the <c>"custom-"</c> prefix; idempotent.</summary>
    public static string EnsureCustomPrefix(string name) =>
        name.StartsWith(CustomPrefix, StringComparison.Ordinal) ? name : CustomPrefix + name;
}
