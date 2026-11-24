using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Web.Services;

/// <summary>Texts the orchestrator stores for the user: display names and status notes without credentials.</summary>
internal static class RunOrchestratorTextExtensions
{
    /// <summary>Display name of the reviewer <paramref name="reviewerName"/> in the snapshot, or the name itself when unknown.</summary>
    public static string ReviewerDisplayName(this CrewSnapshot snapshot, string reviewerName) =>
        snapshot.Reviewers.FirstOrDefault(r => string.Equals(r.Name, reviewerName, StringComparison.Ordinal)) is { } reviewer
        && !string.IsNullOrWhiteSpace(reviewer.DisplayName)
            ? reviewer.DisplayName
            : reviewerName;

    /// <summary>A copy of <paramref name="artifact"/> whose status note has credential-like fragments removed.</summary>
    public static RunArtifact WithRedactedStatus(this RunArtifact artifact) =>
        artifact.StatusMessage is null
            ? artifact
            : new RunArtifact
            {
                Id = artifact.Id,
                RunId = artifact.RunId,
                FinalizerProfileName = artifact.FinalizerProfileName,
                ArtifactType = artifact.ArtifactType,
                Filename = artifact.Filename,
                ContentType = artifact.ContentType,
                SizeBytes = artifact.SizeBytes,
                StorageUri = artifact.StorageUri,
                StatusMessage = artifact.StatusMessage.RedactSecrets(),
                CreatedAt = artifact.CreatedAt,
            };
}
