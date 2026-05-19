using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Infrastructure.Finalizers;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Endpoints;

public static class ArtifactEndpoints
{
    public static IEndpointRouteBuilder MapArtifactEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/runs/{runId:guid}/artifacts/{artifactId:guid}/download",
            async (
                Guid runId,
                Guid artifactId,
                IRunArtifactRepository artifacts,
                IRunService runService,
                ICurrentUserService currentUser,
                IOptions<FinalizerOptions> finalizerOptions) =>
            {
                var artifact = await artifacts.GetByIdAsync(artifactId, CancellationToken.None);
                if (artifact is null || artifact.RunId != runId)
                    return Results.NotFound();

                // Only File artifacts can be downloaded
                if (artifact.ArtifactType != ArtifactType.File)
                    return Results.BadRequest("Only file artifacts can be downloaded.");

                // Owner check: admins pass null to bypass; non-admins pass their username.
                // GetRunAsync returns null when the run belongs to a different user.
                var requestingUsername = currentUser.IsAdmin ? null : currentUser.Username;
                var run = await runService.GetRunAsync(runId, requestingUsername, CancellationToken.None);
                if (run is null)
                    return Results.Forbid();

                var path = artifact.StorageUri;

                // Validate path is within the configured export directory (defense-in-depth)
                var exportRoot = Path.GetFullPath(finalizerOptions.Value.ExportPath);
                var fullPath   = Path.GetFullPath(path);
                if (!fullPath.StartsWith(exportRoot, StringComparison.OrdinalIgnoreCase))
                    return Results.Forbid();

                if (!File.Exists(fullPath))
                    return Results.NotFound("File no longer exists on disk.");

                var contentType = artifact.ContentType ?? "application/octet-stream";
                var filename    = artifact.Filename ?? Path.GetFileName(fullPath);

                return Results.File(
                    fullPath,
                    contentType,
                    fileDownloadName: filename);
            })
            .RequireAuthorization();

        return app;
    }
}
