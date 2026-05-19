using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Mcp.Models;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class ListRunArtifactsTool
{
    [McpServerTool, Description("Lists all artifacts produced by finalizer steps for a specific run. Non-admin users can only access their own runs.")]
    public static async Task<IReadOnlyList<RunArtifactDto>?> ListRunArtifacts(
        IRunService runService,
        ICurrentUserService currentUser,
        IRunArtifactRepository artifactRepository,
        [Description("The run ID (GUID).")] string runId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out var guid)) return null;

        var requestingUsername = currentUser.IsAdmin ? null : currentUser.Username;
        var run = await runService.GetRunDetailsAsync(guid, requestingUsername, cancellationToken);
        if (run is null) return null;

        var artifacts = await artifactRepository.ListByRunAsync(guid, cancellationToken);
        return artifacts.Select(a => new RunArtifactDto(
            a.Id.ToString(),
            a.RunId.ToString(),
            a.FinalizerProfileName,
            a.ArtifactType.ToString(),
            a.Filename,
            a.ContentType,
            a.SizeBytes,
            a.StorageUri,
            a.StatusMessage,
            a.CreatedAt)).ToList();
    }
}
