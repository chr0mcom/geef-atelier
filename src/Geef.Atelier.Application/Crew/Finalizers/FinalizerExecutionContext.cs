using Geef.Atelier.Core.Domain.Crew.Finalizers;

namespace Geef.Atelier.Application.Crew.Finalizers;

/// <summary>
/// Input passed to each <see cref="IFinalizerExecutor"/> during a run's finalization phase.
/// <para><c>FinalText</c> is the original converged output. <c>CurrentText</c> is the working copy
/// updated by each preceding finalizer in the chain.</para>
/// </summary>
public sealed record FinalizerExecutionContext(
    Guid RunId,
    string? TemplateName,
    string FinalText,
    string CurrentText,
    DateTimeOffset RunCompletedAt);
