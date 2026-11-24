namespace Geef.Atelier.Application.Runs;

/// <summary>
/// Thrown by <see cref="IRunService.ResumeRunAsync"/> when a run cannot be continued for a reason the user can act on.
/// The message is a plain-English sentence meant to be shown as is.
/// </summary>
/// <param name="message">User-facing reason.</param>
public sealed class RunNotResumableException(string message) : InvalidOperationException(message)
{
    /// <summary>Message for a run that was never fully prepared and has no usable setup.</summary>
    public const string NotPreparedMessage = "This task was never fully prepared and cannot be continued. Please submit it again.";
}
