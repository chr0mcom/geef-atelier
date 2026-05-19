using System.ComponentModel;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Infrastructure.Finalizers;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Geef.Atelier.Mcp.Tools;

[McpServerToolType]
public static class DownloadRunArtifactTool
{
    [McpServerTool, Description(
        "Downloads a file artifact from a run and returns its content as a Base64-encoded string. " +
        "Only works for ArtifactType=File artifacts. Non-admin users can only access their own runs.")]
    public static async Task<DownloadArtifactResult?> DownloadRunArtifact(
        IRunService runService,
        ICurrentUserService currentUser,
        IRunArtifactRepository artifactRepository,
        IOptions<FinalizerOptions> finalizerOptions,
        [Description("The run ID (GUID).")] string runId,
        [Description("The artifact ID (GUID).")] string artifactId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(runId, out var runGuid) || !Guid.TryParse(artifactId, out var artifactGuid))
            return null;

        var requestingUsername = currentUser.IsAdmin ? null : currentUser.Username;
        var run = await runService.GetRunDetailsAsync(runGuid, requestingUsername, cancellationToken);
        if (run is null) return null;

        var artifact = await artifactRepository.GetByIdAsync(artifactGuid, cancellationToken);
        if (artifact is null || artifact.RunId != runGuid) return null;
        if (artifact.ArtifactType != ArtifactType.File) return null;

        var exportRoot = Path.GetFullPath(finalizerOptions.Value.ExportPath);
        var filePath = Path.GetFullPath(
            Path.Combine(exportRoot, runGuid.ToString("N"), artifact.Filename ?? artifactGuid.ToString("N")));

        if (!filePath.StartsWith(exportRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!File.Exists(filePath)) return null;

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        return new DownloadArtifactResult(
            artifact.Filename ?? string.Empty,
            artifact.ContentType ?? "application/octet-stream",
            Convert.ToBase64String(bytes));
    }
}

public sealed record DownloadArtifactResult(
    string Filename,
    string ContentType,
    string ContentBase64);
