using Geef.Sdk.Runtime;

namespace Geef.Atelier.Infrastructure.Llm;

/// <summary>
/// Classifies HTTP-level LLM provider exceptions as transient (worth retrying) or permanent.
/// Used with <see cref="ResilientReviewer"/> to drive retry decisions at the reviewer level.
/// </summary>
internal sealed class HttpTransientFaultClassifier : ITransientFaultClassifier
{
    /// <inheritdoc />
    public bool IsTransient(Exception exception) => LlmResilience.IsTransient(exception);
}
