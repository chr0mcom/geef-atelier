using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// The setup that exists only in code: the direct path (<see cref="DirectTemplate"/>) with its writer and check profiles. Models are
/// never literals here: every LLM role carries a <see cref="ModelSlot"/> and takes its displayed binding from <see cref="ModelMatrix"/>;
/// the concrete model is assigned per run at snapshot-build time (D-066).
/// </summary>
public static class SystemCrew
{
    /// <summary>Name of the code-only template of the direct path.</summary>
    public const string DirectTemplateName = "direct";

    /// <summary>
    /// Template name stored with runs of the removed variant "Extra careful" (D-087). No new run gets it; stored runs are still read,
    /// continued and changed on the direct path.
    /// </summary>
    public const string RetiredOutlineTemplateName = "direct-outline";

    /// <summary>Name of the extra check of <see cref="RetiredOutlineTemplateName"/> in stored snapshots; the direct path leaves it out.</summary>
    public const string RetiredRequestCheckName = "requirements-check";

    /// <summary>True for the template names the direct path runs: <see cref="DirectTemplateName"/> and, for stored runs, <see cref="RetiredOutlineTemplateName"/>.</summary>
    public static bool IsDirectPathTemplate(string? name) =>
        string.Equals(name, DirectTemplateName, StringComparison.Ordinal) || string.Equals(name, RetiredOutlineTemplateName, StringComparison.Ordinal);

    /// <summary>Writer of the direct path: one writing pass from the request and the attached files.</summary>
    public static readonly ExecutorProfile DirectWriterProfile = new(
        Name: "direct-writer",
        DisplayName: "Writer",
        Description: "Writes the requested text in one pass from the request and the attached files; never invents facts.",
        SystemPrompt: SystemPrompts.DirectWriter,
        Provider: ModelMatrix.Primary(ModelSlot.Executor).Provider,
        Model: ModelMatrix.Primary(ModelSlot.Executor).Model,
        MaxTokens: 256000,
        IsSystem: true,
        ModelSlot: ModelSlot.Executor);

    /// <summary>Checks without an AI model; the implementation is injected at pipeline build time, this entry only registers the profile.</summary>
    public static readonly ReviewerProfile DeterministicChecksProfile = new(
        Name: "deterministic-checks",
        DisplayName: "Automatic checks",
        Description: "Checks length, placeholders, forbidden patterns, required terms and language without an AI model. Injected at pipeline build time.",
        SystemPrompt: "",
        Provider: "",
        Model: "",
        MaxTokens: 0,
        IsSystem: true);

    /// <summary>
    /// Fact check of the direct path against the request, the answers, the profile and the files; the implementation and its
    /// versioned prompt are injected at pipeline build time, this entry only registers the profile and its model slot.
    /// </summary>
    public static readonly ReviewerProfile SourceFactCheckProfile = new(
        Name: "source-fact-check",
        DisplayName: "Fact check",
        Description: "Compares the facts in the text with the request, the answers, the profile and the files. Injected at pipeline build time.",
        SystemPrompt: "Source fact check -- injected at pipeline build time.",
        Provider: ModelMatrix.Primary(ModelSlot.FactReviewer).Provider,
        Model: ModelMatrix.Primary(ModelSlot.FactReviewer).Model,
        MaxTokens: 8192,
        IsSystem: true,
        ModelSlot: ModelSlot.FactReviewer);

    /// <summary>Code-only template of the direct path.</summary>
    public static readonly CrewTemplate DirectTemplate = new(
        Name: DirectTemplateName,
        DisplayName: "Direct",
        Description: "One writing pass with all files in full text, automatic checks without a model and a fact check by a model of another family, at most one correction.",
        ExecutorProfileName: DirectWriterProfile.Name,
        ReviewerProfileNames: [DeterministicChecksProfile.Name, SourceFactCheckProfile.Name],
        EvaluationStrategy: EvaluationStrategy.Parallel,
        ConvergenceOverride: new ConvergencePolicyOverride(MaxIterations: 2, AbortOnCritical: false, DetectRegression: false, StagnationThreshold: 2),
        Domain: ModelDomain.General);

    /// <summary>Executor profiles that exist only in code, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ExecutorProfile> CodeOnlyExecutorProfiles =
        new Dictionary<string, ExecutorProfile>
        {
            [DirectWriterProfile.Name] = DirectWriterProfile,
        };

    /// <summary>Reviewer profiles that exist only in code, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, ReviewerProfile> CodeOnlyReviewerProfiles =
        new Dictionary<string, ReviewerProfile>
        {
            [DeterministicChecksProfile.Name] = DeterministicChecksProfile,
            [SourceFactCheckProfile.Name] = SourceFactCheckProfile,
        };

    /// <summary>Crew templates that exist only in code, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, CrewTemplate> CodeOnlyTemplates =
        new Dictionary<string, CrewTemplate>
        {
            [DirectTemplate.Name] = DirectTemplate,
        };
}
