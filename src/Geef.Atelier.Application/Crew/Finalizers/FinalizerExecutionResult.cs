using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Application.Crew.Finalizers;

/// <summary>
/// Result returned by a single <see cref="IFinalizerExecutor"/> step.
/// </summary>
/// <param name="UpdatedText">
/// Replacement for the current text when the finalizer transforms it (e.g. anti-AI-voice, enrichment).
/// Null when the finalizer does not modify the text (e.g. file export, external sink).
/// </param>
/// <param name="Artifact">
/// A <see cref="RunArtifact"/> record to persist, if the finalizer produced one.
/// Null for pure-transform finalizers.
/// </param>
/// <param name="CostEur">
/// LLM cost incurred by this finalizer step, if any (only non-zero for <see cref="FinalizerType.Transform"/>).
/// </param>
/// <param name="ActorName">Name of the finalizer profile that produced this result (for cost attribution).</param>
/// <param name="ModelName">Model used for cost attribution; empty string for non-LLM finalizers.</param>
/// <param name="InputTokens">Input tokens consumed; 0 for non-LLM finalizers.</param>
/// <param name="OutputTokens">Output tokens consumed; 0 for non-LLM finalizers.</param>
/// <param name="ProviderName">Provider that served this finalizer step; null for non-LLM finalizers.</param>
public sealed record FinalizerExecutionResult(
    string? UpdatedText,
    RunArtifact? Artifact,
    decimal? CostEur,
    string ActorName,
    string ModelName = "",
    int InputTokens = 0,
    int OutputTokens = 0,
    string? ProviderName = null);
