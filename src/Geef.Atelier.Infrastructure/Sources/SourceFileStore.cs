using System.Security.Cryptography;
using System.Text;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>Stores original files on the sources volume. Paths are built only from the owner hash, the source id and a fixed extension.</summary>
internal interface ISourceFileStore
{
    /// <summary>
    /// Moves the received file <paramref name="receivedPath"/> into the store and returns its path relative to
    /// <see cref="SourceOptions.StorageRoot"/>.
    /// </summary>
    Task<string> SaveFromFileAsync(string ownerUsername, Guid sourceId, string extension, string receivedPath, CancellationToken ct);

    /// <summary>Deletes a stored file; false when it did not exist or the path lies outside the root. Never throws for IO problems.</summary>
    bool TryDelete(string relativePath);

    /// <summary>Resolves a stored path to a full path inside the root, or <c>null</c> when it would leave the root.</summary>
    string? Resolve(string relativePath);
}

internal sealed class SourceFileStore(IOptions<SourceOptions> options, ILogger<SourceFileStore> logger) : ISourceFileStore
{
    public Task<string> SaveFromFileAsync(string ownerUsername, Guid sourceId, string extension, string receivedPath, CancellationToken ct)
    {
        if (extension is not (".pdf" or ".docx" or ".txt" or ".md"))
            throw new ArgumentException("Unknown storage extension.", nameof(extension));
        ct.ThrowIfCancellationRequested();

        var relative = $"{OwnerFolder(ownerUsername)}/{sourceId:N}{extension}";
        var full = Resolve(relative) ?? throw new InvalidOperationException("The storage path leaves the sources root.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.Move(receivedPath, full, overwrite: true);
        return Task.FromResult(relative);
    }

    public bool TryDelete(string relativePath)
    {
        var full = Resolve(relativePath);
        if (full is null)
        {
            logger.LogWarning("Refused to delete a source file outside the sources root.");
            return false;
        }

        try
        {
            if (!File.Exists(full))
                return false;
            File.Delete(full);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning("Source file could not be deleted: {Error}", ex.GetType().Name);
            return false;
        }
    }

    public string? Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            return null;

        var root = Path.GetFullPath(options.Value.StorageRoot);
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        return full.StartsWith(rootWithSeparator, StringComparison.Ordinal) ? full : null;
    }

    internal static string OwnerFolder(string ownerUsername) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ownerUsername)))[..16];
}
