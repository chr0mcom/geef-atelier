namespace Geef.Atelier.Core.Domain.Crew.Models;

/// <summary>
/// Single source of truth for which model serves which role in which domain (D-066). Each (slot, domain)
/// pair maps to an ordered candidate list; the application layer picks the first candidate that is live,
/// respects model plurality and spreads same-role reviewers across a ring. Model ids live only here. Claude and
/// Codex candidates are always-latest aliases (<c>claude-opus-latest</c>, <c>gpt-sol-latest</c>) that the cli-proxy
/// resolves to the newest model of the line, so a model release needs no code change; run snapshots keep the
/// resolved concrete id.
/// <para>
/// Basis: model research 2026-11-22 (<c>reports/2026-11-22-modellrecherche.md</c>) and the Fable
/// advisory addendum in <c>docs/superpowers/specs/2026-11-22-modellmatrix-design.md</c>. Claude and
/// OpenAI models are always routed through the subscription CLIs.
/// </para>
/// </summary>
public static class ModelMatrix
{
    /// <summary>Provider name of the Claude subscription CLI.</summary>
    public const string ClaudeCli = "claude-cli";

    /// <summary>Provider name of the Codex (OpenAI) subscription CLI.</summary>
    public const string CodexCli = "codex-cli";

    /// <summary>Always-latest alias of the Codex Sol line, resolved by the cli-proxy to the newest listed Sol model.</summary>
    public const string CodexSolAlias = "gpt-sol-latest";

    /// <summary>Always-latest alias of the Codex Astra line.</summary>
    public const string CodexAstraAlias = "gpt-astra-latest";

    /// <summary>Always-latest alias of the Codex Terra line.</summary>
    public const string CodexTerraAlias = "gpt-terra-latest";

    /// <summary>Codex model that replaces retired OpenAI ids without a Sol or Terra line when a profile is rerouted.</summary>
    public const string RetiredOpenAiReplacement = CodexAstraAlias;

    /// <summary>Number of eligible candidates same-role reviewers of one crew rotate through.</summary>
    public const int ReviewerRingSize = 3;

    /// <summary>Upper bound of pay-per-token reviewers per crew.</summary>
    public const int MaxPaidReviewersPerCrew = 2;

    /// <summary>Outside Legal/Academic, paid candidates join the ring only from this many same-role reviewers on.</summary>
    public const int PaidOutsideCoreDomainsMinRoleCount = 3;

    private static readonly ModelCandidate Opus = new(ClaudeCli, "claude-opus-latest");
    private static readonly ModelCandidate Sonnet = new(ClaudeCli, "claude-sonnet-latest");
    private static readonly ModelCandidate Haiku = new(ClaudeCli, "claude-haiku-latest");
    private static readonly ModelCandidate Fable = new(ClaudeCli, "claude-fable-latest");
    private static readonly ModelCandidate Astra = new(CodexCli, CodexAstraAlias);
    private static readonly ModelCandidate Sol = new(CodexCli, CodexSolAlias);
    private static readonly ModelCandidate Neon = new("openrouter", "google/gemini-4-neon");

    /// <summary>
    /// Codex models the subscription serves (always-latest aliases first, then the concrete ids known at release);
    /// used to keep an OpenAI id when rerouting it to <see cref="CodexCli"/>.
    /// </summary>
    public static IReadOnlyList<string> CodexSubscriptionModels { get; } =
        [CodexSolAlias, CodexAstraAlias, CodexTerraAlias, "gpt-6.2-sol", "gpt-6.2-astra", "gpt-6.1-terra", "gpt-6.1-sol", "gpt-6-astra", "gpt-6-sol"];

    private static readonly IReadOnlyList<ModelCandidate> ProseExecutor = [Opus, Sol];
    private static readonly IReadOnlyList<ModelCandidate> TechnicalExecutor = [Fable, Opus, Astra];
    private static readonly IReadOnlyList<ModelCandidate> FactReviewer = [Astra, Opus, Neon, Sonnet];
    private static readonly IReadOnlyList<ModelCandidate> StyleReviewer = [Sol, Opus, Sonnet];
    private static readonly IReadOnlyList<ModelCandidate> DomainReviewerCore = [Neon, Astra, Opus, Fable, Sonnet];
    private static readonly IReadOnlyList<ModelCandidate> DomainReviewerOther = [Astra, Neon, Opus, Fable, Sonnet];
    private static readonly IReadOnlyList<ModelCandidate> StrategicAdvisor = [Fable, Astra];
    private static readonly IReadOnlyList<ModelCandidate> CriticalAdvisor = [Fable, Astra];
    private static readonly IReadOnlyList<ModelCandidate> TechnicalExpert = [Fable, Astra];
    private static readonly IReadOnlyList<ModelCandidate> AcademicExpert = [Astra, Opus];
    private static readonly IReadOnlyList<ModelCandidate> GeneralExpert = [Opus, Astra];
    private static readonly IReadOnlyList<ModelCandidate> GroundingRefiner = [Sonnet, Sol];
    private static readonly IReadOnlyList<ModelCandidate> TransformRewrite = [Sol, Sonnet];
    private static readonly IReadOnlyList<ModelCandidate> TransformCondense = [Sonnet, Sol];
    private static readonly IReadOnlyList<ModelCandidate> Composer = [Opus, Sonnet, Sol];
    private static readonly IReadOnlyList<ModelCandidate> Helper = [Haiku, Sonnet];
    private static readonly IReadOnlyList<ModelCandidate> Intake = [Sonnet, Sol];

    /// <summary>Ordered candidates for <paramref name="slot"/> in <paramref name="domain"/>; never empty.</summary>
    public static IReadOnlyList<ModelCandidate> CandidatesFor(ModelSlot slot, ModelDomain domain = ModelDomain.General) =>
        slot switch
        {
            ModelSlot.Executor => domain == ModelDomain.Technical ? TechnicalExecutor : ProseExecutor,
            ModelSlot.FactReviewer => FactReviewer,
            ModelSlot.StyleReviewer => StyleReviewer,
            ModelSlot.DomainReviewer => domain.IsCoreDomain() ? DomainReviewerCore : DomainReviewerOther,
            ModelSlot.StrategicAdvisor => StrategicAdvisor,
            ModelSlot.CriticalAdvisor => CriticalAdvisor,
            ModelSlot.DomainExpertAdvisor => domain switch
            {
                ModelDomain.Technical => TechnicalExpert,
                ModelDomain.Academic => AcademicExpert,
                _ => GeneralExpert,
            },
            ModelSlot.GroundingRefiner => GroundingRefiner,
            ModelSlot.TransformRewrite => TransformRewrite,
            ModelSlot.TransformCondense => TransformCondense,
            ModelSlot.Composer => Composer,
            ModelSlot.Helper => Helper,
            ModelSlot.Intake => Intake,
            _ => ProseExecutor,
        };

    /// <summary>First candidate of <paramref name="slot"/> in <paramref name="domain"/>, i.e. the binding shown before run-time resolution.</summary>
    public static ModelCandidate Primary(ModelSlot slot, ModelDomain domain = ModelDomain.General) =>
        CandidatesFor(slot, domain)[0];

    /// <summary>Every distinct candidate across all slots and domains.</summary>
    public static IEnumerable<ModelCandidate> All =>
        Enum.GetValues<ModelSlot>()
            .SelectMany(slot => Enum.GetValues<ModelDomain>().SelectMany(domain => CandidatesFor(slot, domain)))
            .Distinct();

    /// <summary>True for domains where paid candidates are worth it regardless of crew size (Legal, Academic).</summary>
    public static bool IsCoreDomain(this ModelDomain domain) =>
        domain is ModelDomain.Legal or ModelDomain.Academic;
}
