using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Web.Services;

/// <summary>Texts the orchestrator stores for the user.</summary>
internal static class RunOrchestratorTextExtensions
{
    /// <summary>Display name of the reviewer <paramref name="reviewerName"/> in the snapshot, or the name itself when unknown.</summary>
    public static string ReviewerDisplayName(this CrewSnapshot snapshot, string reviewerName) =>
        snapshot.Reviewers.FirstOrDefault(r => string.Equals(r.Name, reviewerName, StringComparison.Ordinal)) is { } reviewer
        && !string.IsNullOrWhiteSpace(reviewer.DisplayName)
            ? reviewer.DisplayName
            : reviewerName;
}
