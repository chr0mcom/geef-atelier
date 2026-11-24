using System.Buffers.Binary;
using System.IO.Compression;
using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>Result of looking at the first bytes of a file: its kind, or why it is not supported.</summary>
internal sealed record DetectedKind(SourceKind? Kind, string? Reason);

/// <summary>
/// Determines the kind of a file from its content: <c>%PDF-</c> within the first 1,024 bytes (on purpose: readers accept
/// the PDF header anywhere there, so a text file that mentions <c>%PDF-</c> that early counts as a PDF and is refused as
/// unreadable), a ZIP archive with <c>word/document.xml</c>, or text. Before a ZIP archive is opened in the web process,
/// its end record is read: more entries than the limit, a central directory larger than 1 KiB per allowed entry or a
/// ZIP64 directory (no Word file needs one) refuse the file without listing the archive. The file name only decides
/// between text and Markdown and is never trusted for anything else.
/// </summary>
internal static class SourceKindDetector
{
    internal const string UnsupportedReason = "This file type is not supported. Use PDF, Word (.docx), text or Markdown.";
    internal const string OldWordReason = "Old Word files (.doc) are not supported. Please save the file as .docx.";
    private const int SniffBytes = 8_192;
    private const int EndRecordBytes = 22;
    private const uint EndRecordSignature = 0x06054B50;
    private const long DirectoryBytesPerEntry = 1_024;

    /// <summary>Detects the kind of <paramref name="content"/>; a ZIP archive may have at most <paramref name="maxArchiveEntries"/> entries.</summary>
    public static DetectedKind Detect(ReadOnlySpan<byte> content, string fileName, int maxArchiveEntries = 10_000)
    {
        var bytes = content.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B]) ? content.ToArray() : [];
        return Detect(content, fileName, () => WordArchiveKind(new MemoryStream(bytes, writable: false), maxArchiveEntries));
    }

    /// <summary>Detects the kind of the file at <paramref name="path"/> from its first bytes <paramref name="head"/>; a ZIP archive may have at most <paramref name="maxArchiveEntries"/> entries.</summary>
    public static DetectedKind DetectFile(string path, ReadOnlySpan<byte> head, string fileName, int maxArchiveEntries) =>
        Detect(head, fileName, () => WordArchiveKind(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920), maxArchiveEntries));

    private static DetectedKind Detect(ReadOnlySpan<byte> content, string fileName, Func<DetectedKind> wordArchiveKind)
    {
        if (content.Length == 0)
            return new(null, "This file is empty.");

        var head = content[..Math.Min(1_024, content.Length)];
        if (head.IndexOf("%PDF-"u8) >= 0)
            return new(SourceKind.Pdf, null);

        if (content.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04]))
            return wordArchiveKind();

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

    private static DetectedKind WordArchiveKind(Stream content, int maxArchiveEntries)
    {
        try
        {
            if (!DirectoryIsSmall(content, maxArchiveEntries))
            {
                content.Dispose();
                return new(null, DocxSourceExtractor.ArchiveReason);
            }

            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: false);
            return archive.GetEntry("word/document.xml") is not null ? new(SourceKind.Docx, null) : new(null, UnsupportedReason);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
        {
            content.Dispose();
            return new(null, UnsupportedReason);
        }
    }

    private static bool DirectoryIsSmall(Stream content, int maxArchiveEntries)
    {
        var length = content.Length;
        if (length < EndRecordBytes)
            return true;

        var tail = new byte[(int)Math.Min(length, EndRecordBytes + ushort.MaxValue)];
        content.Seek(length - tail.Length, SeekOrigin.Begin);
        content.ReadExactly(tail);
        content.Seek(0, SeekOrigin.Begin);
        for (var at = tail.Length - EndRecordBytes; at >= 0; at--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(at)) != EndRecordSignature)
                continue;
            var entries = BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(at + 10));
            var directoryBytes = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(at + 12));
            if (entries == ushort.MaxValue || directoryBytes == uint.MaxValue)
                return false;
            return entries <= maxArchiveEntries && directoryBytes <= DirectoryBytesPerEntry * Math.Max(1, maxArchiveEntries);
        }

        return true;
    }
}
