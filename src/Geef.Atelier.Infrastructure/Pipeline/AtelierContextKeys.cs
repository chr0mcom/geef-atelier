using Geef.Atelier.Core.Domain.Checks;
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
    /// Injected by <c>OrderGroundingStep</c> for a run with a seed draft (a resumed run or a change of an earlier version): the text
    /// to start from. <c>DirectWriterExecutor</c> uses it on the first pass instead of writing from scratch.
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
