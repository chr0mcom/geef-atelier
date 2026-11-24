using System.Runtime.InteropServices;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>Helpers for the byte buffers handed to the extractors.</summary>
internal static class ExtractionMemoryExtensions
{
    /// <summary>Returns the array behind <paramref name="content"/> when it covers the whole array, otherwise a copy.</summary>
    public static byte[] AsArray(this ReadOnlyMemory<byte> content) =>
        MemoryMarshal.TryGetArray(content, out var segment) && segment.Array is { } array && segment.Offset == 0 && segment.Count == array.Length
            ? array
            : content.ToArray();
}
