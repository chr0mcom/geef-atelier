namespace Geef.Atelier.Mcp.Models;

/// <summary>How the facts of the delivered text were checked.</summary>
/// <param name="State"><c>checked</c> (nothing found and every flagged passage listed), <c>open_points</c> (passages need the person's attention, or flagged passages could not be checked or listed), <c>not_completed</c> (the check failed), <c>version_not_checked</c> (the check ran on an earlier wording; the facts of the delivered text were not checked) or <c>not_checked</c>.</param>
/// <param name="FilesCompared">True only when the facts were compared with the content of the person's files; false when there were no files, when they were too long to compare in full, or when the facts were not checked.</param>
public sealed record OrderFactCheckDto(string State, bool FilesCompared);
