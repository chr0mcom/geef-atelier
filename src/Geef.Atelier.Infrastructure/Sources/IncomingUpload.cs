using System.Security.Cryptography;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>
/// An upload streamed into a private temporary directory below <see cref="IncomingFolder"/> of the sources root
/// (directory 0700, file 0600), hashed on the way and capped at a size. Disposing removes the directory with whatever is
/// still in it; the file itself is moved into the store when the source is kept.
/// </summary>
internal sealed class IncomingUpload(string directory, string filePath, long length, string sha256, byte[] head) : IAsyncDisposable
{
    /// <summary>Name of the folder below the sources root that holds uploads in progress.</summary>
    public const string IncomingFolder = ".incoming";

    /// <summary>Name of the received file inside its directory.</summary>
    public const string FileName = "upload.part";

    private const int HeadBytes = 8_192;
    private const UnixFileMode PrivateDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode PrivateFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>The private temporary directory of this upload; also the working directory of its extraction.</summary>
    public string Directory { get; } = directory;

    /// <summary>Full path of the received file.</summary>
    public string FilePath { get; } = filePath;

    /// <summary>Size of the received file in bytes.</summary>
    public long Length { get; } = length;

    /// <summary>Lower-case hex SHA-256 of the received file.</summary>
    public string Sha256 { get; } = sha256;

    /// <summary>The first bytes of the file (at most 8 KiB), enough to detect its kind.</summary>
    public byte[] Head { get; } = head;

    /// <summary>
    /// Streams <paramref name="content"/> into a new directory below <paramref name="storageRoot"/>; <c>null</c> when it is
    /// larger than <paramref name="maxBytes"/> (nothing is left behind then).
    /// </summary>
    public static async Task<IncomingUpload?> ReceiveAsync(Stream content, string storageRoot, long maxBytes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(content);
        var directory = Path.Combine(Path.GetFullPath(storageRoot), IncomingFolder, Guid.NewGuid().ToString("N"));
        CreatePrivateDirectory(directory);
        var filePath = Path.Combine(directory, FileName);
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var head = new MemoryStream(HeadBytes);
            long length = 0;
            await using (var file = new FileStream(filePath, FileOptionsFor()))
            {
                var chunk = new byte[81_920];
                int read;
                while ((read = await content.ReadAsync(chunk, ct)) > 0)
                {
                    length += read;
                    if (length > maxBytes)
                    {
                        await file.DisposeAsync();
                        DeleteDirectory(directory);
                        return null;
                    }

                    hash.AppendData(chunk, 0, read);
                    if (head.Length < HeadBytes)
                        head.Write(chunk, 0, (int)Math.Min(read, HeadBytes - head.Length));
                    await file.WriteAsync(chunk.AsMemory(0, read), ct);
                }
            }

            return new IncomingUpload(directory, filePath, length, Convert.ToHexStringLower(hash.GetHashAndReset()), head.ToArray());
        }
        catch
        {
            DeleteDirectory(directory);
            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        DeleteDirectory(Directory);
        return ValueTask.CompletedTask;
    }

    internal static void DeleteDirectory(string directory)
    {
        try
        {
            if (System.IO.Directory.Exists(directory))
                System.IO.Directory.Delete(directory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void CreatePrivateDirectory(string directory)
    {
        if (OperatingSystem.IsWindows())
            System.IO.Directory.CreateDirectory(directory);
        else
            System.IO.Directory.CreateDirectory(directory, PrivateDirectory);
    }

    private static FileStreamOptions FileOptionsFor()
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
            BufferSize = 81_920,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = PrivateFile;
        return options;
    }
}
