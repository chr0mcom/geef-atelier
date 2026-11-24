using System.IO.Compression;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>Result of looking at the first bytes of a file: its kind, or why it is not supported.</summary>
internal sealed record DetectedKind(SourceKind? Kind, string? Reason);

/// <summary>
/// Determines the kind of a file from its content: <c>%PDF-</c> near the start, a ZIP archive with
/// <c>word/document.xml</c>, or text. The file name only decides between text and Markdown and is never trusted for
/// anything else.
/// </summary>
internal static class SourceKindDetector
{
    internal const string UnsupportedReason = "This file type is not supported. Use PDF, Word (.docx), text or Markdown.";
    internal const string OldWordReason = "Old Word files (.doc) are not supported. Please save the file as .docx.";
    private const int SniffBytes = 8_192;

    /// <summary>Detects the kind of <paramref name="content"/>.</summary>
    public static DetectedKind Detect(ReadOnlySpan<byte> content, string fileName)
    {
        var bytes = content.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B]) ? content.ToArray() : [];
        return Detect(content, fileName, () => IsWordArchive(new MemoryStream(bytes, writable: false)));
    }

    /// <summary>Detects the kind of the file at <paramref name="path"/> from its first bytes <paramref name="head"/>.</summary>
    public static DetectedKind DetectFile(string path, ReadOnlySpan<byte> head, string fileName) =>
        Detect(head, fileName, () => IsWordArchive(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920)));

    private static DetectedKind Detect(ReadOnlySpan<byte> content, string fileName, Func<bool> isWordArchive)
    {
        if (content.Length == 0)
            return new(null, "This file is empty.");

        var head = content[..Math.Min(1_024, content.Length)];
        if (head.IndexOf("%PDF-"u8) >= 0)
            return new(SourceKind.Pdf, null);

        if (content.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04]))
            return isWordArchive() ? new(SourceKind.Docx, null) : new(null, UnsupportedReason);

        if (content.StartsWith((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0]))
            return new(null, OldWordReason);

        if (!LooksLikeText(content))
            return new(null, UnsupportedReason);

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return new(extension is ".md" or ".markdown" ? SourceKind.Markdown : SourceKind.Text, null);
    }

    /// <summary>Returns the MIME type stored for <paramref name="kind"/>.</summary>
    public static string ContentType(this SourceKind kind) => kind switch
    {
        SourceKind.Pdf => "application/pdf",
        SourceKind.Docx => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        SourceKind.Markdown => "text/markdown",
        _ => "text/plain",
    };

    /// <summary>Returns the file extension used on the sources volume for <paramref name="kind"/>.</summary>
    public static string StorageExtension(this SourceKind kind) => kind switch
    {
        SourceKind.Pdf => ".pdf",
        SourceKind.Docx => ".docx",
        SourceKind.Markdown => ".md",
        _ => ".txt",
    };

    /// <summary>True when the bytes look like UTF-16 without a byte order mark (many zero bytes at even or odd positions).</summary>
    public static bool LooksLikeUtf16(ReadOnlySpan<byte> content, out bool littleEndian)
    {
        littleEndian = true;
        var sample = content[..Math.Min(SniffBytes, content.Length)];
        if (sample.Length < 4)
            return false;

        int evenZeros = 0, oddZeros = 0;
        for (var i = 0; i < sample.Length; i++)
        {
            if (sample[i] != 0)
                continue;
            if (i % 2 == 0)
                evenZeros++;
            else
                oddZeros++;
        }

        var half = sample.Length / 2;
        if (oddZeros > half * 0.3 && evenZeros < half * 0.05)
            return true;
        if (evenZeros > half * 0.3 && oddZeros < half * 0.05)
        {
            littleEndian = false;
            return true;
        }

        return false;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> content)
    {
        if (content.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || content.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF])
            || content.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) || LooksLikeUtf16(content, out _))
            return true;

        var sample = content[..Math.Min(SniffBytes, content.Length)];
        var control = 0;
        foreach (var b in sample)
        {
            if (b == 0)
                return false;
            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0C or 0x0D))
                control++;
        }

        return control <= sample.Length / 100;
    }

    private static bool IsWordArchive(Stream content)
    {
        try
        {
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: false);
            return archive.GetEntry("word/document.xml") is not null;
        }
        catch (InvalidDataException)
        {
            content.Dispose();
            return false;
        }
    }
}
