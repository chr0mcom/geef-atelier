namespace Geef.Atelier.Core.Domain.Crew;

/// <summary>
/// Thrown when the stored crew configuration of a run is missing, unreadable or incomplete. The message is a
/// plain-English sentence meant for the user; the run fails with it instead of silently running another crew.
/// </summary>
public sealed class InvalidCrewSnapshotException(string message, Exception? innerException = null)
    : Exception(message, innerException);
