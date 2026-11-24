namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>
/// Stops a direct-path run with a plain-English message that is shown to the person as it is. Technical details
/// belong in the log, never in <see cref="Exception.Message"/>.
/// </summary>
internal sealed class DirectPathException(string message) : Exception(message)
{
    /// <summary>The writer answered twice without a usable text.</summary>
    public const string WriterReturnedNoText = "The writer returned no text. Please try again.";

    /// <summary>The writing service is unavailable and the run may not switch to another one.</summary>
    public const string WritingServiceBusy = "The writing service is busy. Please try again in a few minutes.";

    /// <summary>A direct-path run without an order cannot find its files.</summary>
    public const string NoOrder = "This task has no order, so its files cannot be found.";
}
