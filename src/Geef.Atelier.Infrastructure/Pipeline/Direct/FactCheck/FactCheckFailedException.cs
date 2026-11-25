namespace Geef.Atelier.Infrastructure.Pipeline.Direct.FactCheck;

/// <summary>
/// Thrown by <see cref="SourceFactCheckReviewer"/> when neither the model nor its fallback could complete the check. The SDK
/// isolates the reviewer as failed, the run warns that the check could not be completed and the text is delivered. The message
/// names only error codes, never model text.
/// </summary>
internal sealed class FactCheckFailedException(string message) : Exception(message);
