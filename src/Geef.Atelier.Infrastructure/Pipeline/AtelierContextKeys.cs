using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Crew.Profiles;
using Geef.Atelier.Core.Domain.FactCheck;
using Geef.Atelier.Core.Domain.Writing;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Pipeline.Direct;
using Geef.Sdk.Context;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class AtelierContextKeys
{
    public static readonly ContextKey<string>         GroundedBrief = new("geef:atelier:grounded-brief");
    public static readonly ContextKey<string>         CurrentDraft  = new("geef:atelier:current-draft");
    public static readonly ContextKey<LlmTokenUsage>  TokenUsage    = new("geef:atelier:token-usage");

    /// <summary>
    /// The most complete draft produced so far across all iterations (the high-water mark by length).
    /// <c>ProfileBasedExecutor</c> uses it as the yardstick for detecting a draft collapse (changelog /
    /// partial document) and as the fallback when the executor repeatedly fails to re-emit the full text,
    /// guaranteeing the working draft never regresses below the most complete version seen.
    /// </summary>
    public static readonly ContextKey<string>         BestDraft     = new("geef:atelier:best-draft");

    /// <summary>
    /// The executor profile to use for LLM calls. Set by the composition pipeline factory so that
    /// <see cref="Geef.Atelier.Infrastructure.Composition.CrewComposerExecutor"/> can resolve the
    /// correct provider/model without requiring it as a constructor argument.
    /// </summary>
    public static readonly ContextKey<ExecutorProfile> CompositionExecutorProfile = new("geef:atelier:composition-executor-profile");

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

    /// <summary>
    /// Injected by <c>OrderGroundingStep</c> on the direct path: the rendered source block of the order, handed to the
    /// writer as background document.
    /// </summary>
    public static readonly ContextKey<OrderSourceBundle> OrderSources = new("geef:atelier:order-sources");

    /// <summary>
    /// Injected by <c>OrderGroundingStep</c> on the direct path: the constraints read from the request in the person's
    /// own words, checked by <c>DeterministicChecksReviewer</c>.
    /// </summary>
    public static readonly ContextKey<OrderConstraints> OrderConstraints = new("geef:atelier:order-constraints");

    /// <summary>
    /// Set by <c>DirectWriterExecutor</c> after the first writing pass when it could judge from the input tokens whether
    /// the attached files were read in full; read by the direct-path report.
    /// </summary>
    public static readonly ContextKey<ReadEvidence> ReadEvidence = new("geef:atelier:read-evidence");

    /// <summary>Set by <c>DirectWriterExecutor</c> after each writing pass: the text split from the writer's notes.</summary>
    public static readonly ContextKey<WriterNotesSplit> WriterNotes = new("geef:atelier:writer-notes");

    /// <summary>Injected by <c>OrderGroundingStep</c> on the direct path: everything the text may rely on, for the fact check.</summary>
    public static readonly ContextKey<FactMaterial> FactMaterial = new("geef:atelier:fact-material");

    /// <summary>Injected by <c>OrderGroundingStep</c> on the direct path: the fact-check mode of the run.</summary>
    public static readonly ContextKey<FactCheckMode> FactCheckMode = new("geef:atelier:fact-check-mode");

    /// <summary>Injected by <c>OrderGroundingStep</c> on the direct path: totals in the files that were recalculated.</summary>
    public static readonly ContextKey<SourceArithmeticResult> SourceArithmetic = new("geef:atelier:source-arithmetic");

    /// <summary>
    /// Injected by <c>OrderGroundingStep</c> on the direct path for a run that changes an earlier version: the change the person asked
    /// for (<c>RunEntity.RevisionHint</c>). Its presence makes the writer use <c>DirectWriterPrompts.RevisionInstruction</c>.
    /// </summary>
    public static readonly ContextKey<string> RevisionHint = new("geef:atelier:revision-hint");
}
