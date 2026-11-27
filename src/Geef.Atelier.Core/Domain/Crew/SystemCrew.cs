using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Core.Domain.Crew.Profiles;

namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// The setups that exist only in code: the direct path (<see cref="DirectTemplate"/>) and "Extra careful" with an outline first
/// (<see cref="DirectOutlineTemplate"/>, D-087), with their writer and check profiles. Models are never literals here: every LLM role
/// carries a <see cref="ModelSlot"/> and takes its displayed binding from <see cref="ModelMatrix"/>; the concrete model is assigned per
/// run at snapshot-build time (D-066).
/// </summary>
public static class SystemCrew
{
    /// <summary>Name of the code-only template of the direct path.</summary>
    public const string DirectTemplateName = "direct";

    /// <summary>Name of the code-only template of "Extra careful" with an outline first (<c>Orders:CarefulOutline</c>, D-087).</summary>
    public const string DirectOutlineTemplateName = "direct-outline";

    /// <summary>True for the templates the direct path runs: <see cref="DirectTemplateName"/> and <see cref="DirectOutlineTemplateName"/>.</summary>
    public static bool IsDirectPathTemplate(string? name) =>
        string.Equals(name, DirectTemplateName, StringComparison.Ordinal) || string.Equals(name, DirectOutlineTemplateName, StringComparison.Ordinal);

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

    /// <summary>
    /// Check of "Extra careful" with an outline (D-087, repair): lists the explicit requirements of the request word for word and says
    /// whether the first version meets them; the implementation is injected at pipeline build time. Fixed to the standard model of the
    /// intake (another model than the writer), without a slot, so that the model assignment keeps it.
    /// </summary>
    public static readonly ReviewerProfile RequirementsCheckProfile = new(
        Name: "requirements-check",
        DisplayName: "Request check",
        Description: "Checks whether the first version meets every explicit requirement of the request. Injected at pipeline build time.",
        SystemPrompt: "Requirements check -- injected at pipeline build time.",
        Provider: ModelMatrix.Primary(ModelSlot.Intake).Provider,
        Model: ModelMatrix.Primary(ModelSlot.Intake).Model,
        MaxTokens: 8192,
        IsSystem: true);

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

    /// <summary>
    /// Code-only template of "Extra careful" with an outline first (D-087): the direct template under its own name, so that the run
    /// records that one call planned sections with word budgets before the writing pass. Same writer, checks, fact check and passes, plus
    /// the check of the explicit requirements of the request after the first version (<see cref="RequirementsCheckProfile"/>, repair of D-087).
    /// </summary>
    public static readonly CrewTemplate DirectOutlineTemplate = DirectTemplate with
    {
        Name = DirectOutlineTemplateName,
        DisplayName = "Extra careful",
        Description = "Plans the text in sections with word budgets first, then one writing pass along that plan, with the same automatic checks and fact check as the direct path and a check that every explicit requirement of the request is met, followed by at most one correction.",
        ReviewerProfileNames = [DeterministicChecksProfile.Name, SourceFactCheckProfile.Name, RequirementsCheckProfile.Name],
    };

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
            [RequirementsCheckProfile.Name] = RequirementsCheckProfile,
        };

    /// <summary>Crew templates that exist only in code, indexed by name.</summary>
    public static readonly IReadOnlyDictionary<string, CrewTemplate> CodeOnlyTemplates =
        new Dictionary<string, CrewTemplate>
        {
            [DirectTemplate.Name] = DirectTemplate,
            [DirectOutlineTemplate.Name] = DirectOutlineTemplate,
        };
}
