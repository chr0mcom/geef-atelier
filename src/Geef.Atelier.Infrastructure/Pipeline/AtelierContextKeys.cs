using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Context;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class AtelierContextKeys
{
    public static readonly ContextKey<string>         GroundedBrief = new("geef:atelier:grounded-brief");
    public static readonly ContextKey<string>         CurrentDraft  = new("geef:atelier:current-draft");
    public static readonly ContextKey<LlmTokenUsage>  TokenUsage    = new("geef:atelier:token-usage");

    /// <summary>
    /// The executor profile to use for LLM calls. Set by the composition pipeline factory so that
    /// <see cref="Geef.Atelier.Infrastructure.Composition.CrewComposerExecutor"/> can resolve the
    /// correct provider/model without requiring it as a constructor argument.
    /// </summary>
    public static readonly ContextKey<ExecutorProfile> CompositionExecutorProfile = new("geef:atelier:composition-executor-profile");

    /// <summary>
    /// Injected by <c>AdvisorAwareExecutor</c> (or directly by the orchestrator for convergence-failure retries).
    /// Contains a formatted block of advisor consultation outputs that <c>ProfileBasedExecutor</c> prepends to its prompt.
    /// </summary>
    public static readonly ContextKey<string>         AdvisorBlock     = new("geef:atelier:advisor-block");

    /// <summary>
    /// Injected by <c>MultiProviderGroundingStep</c> when one or more grounding providers are configured.
    /// Contains concatenated formatted web-research blocks that <c>ProfileBasedExecutor</c> prepends before
    /// the advisor block and user prompt.
    /// </summary>
    public static readonly ContextKey<string>         GroundingContext  = new("geef:atelier:grounding-context");

    /// <summary>
    /// Injected by <c>SeedDraftGroundingStep</c> for resume runs. Contains the artifact text of the
    /// last completed iteration of the parent run. <c>ProfileBasedExecutor</c> uses it on iteration 1
    /// to prime the LLM with a prior draft rather than generating from scratch.
    /// </summary>
    public static readonly ContextKey<string>         SeedDraft        = new("geef:atelier:seed-draft");
}
