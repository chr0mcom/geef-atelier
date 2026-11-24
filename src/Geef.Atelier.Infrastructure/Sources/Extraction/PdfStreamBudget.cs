using System.IO.Compression;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>
/// Rough check before a PDF is opened: unpacks every Flate-compressed stream that is not a picture into nothing and adds
/// up the unpacked sizes, stopping as soon as the limit is passed. It is cheap and catches plain compression bombs early;
/// nested filters and other tricks are left to the limits of the extraction process.
/// </summary>
internal static class PdfStreamBudget
{
    private const int MaxStreamsChecked = 20_000;
    private const int DictionaryLookBack = 2_048;

    /// <summary>True when the non-picture Flate streams of <paramref name="pdf"/> unpack to more than <paramref name="limitBytes"/>.</summary>
    public static bool ExceedsInflatedLimit(this byte[] pdf, long limitBytes)
    {
        ArgumentNullException.ThrowIfNull(pdf);
        if (limitBytes <= 0)
            return false;

        long total = 0;
        var checkedStreams = 0;
        var buffer = new byte[65_536];
        var position = 0;
        while (position < pdf.Length && checkedStreams < MaxStreamsChecked)
        {
            var found = pdf.AsSpan(position).IndexOf("stream"u8);
            if (found < 0)
                break;
            var keyword = position + found;
            position = keyword + 6;
            if (keyword >= 3 && pdf.AsSpan(keyword - 3, 3).SequenceEqual("end"u8))
                continue;

            var start = DataStart(pdf, position);
            if (start < 0 || !IsTextFlateStream(pdf, keyword) || !HasZlibHeader(pdf, start))
                continue;

            checkedStreams++;
            total += InflatedLength(pdf, start, limitBytes - total + 1, buffer);
            if (total > limitBytes)
                return true;
        }

        return false;
    }

    private static int DataStart(byte[] pdf, int afterKeyword)
    {
        if (afterKeyword < pdf.Length && pdf[afterKeyword] == '\r')
            afterKeyword++;
        if (afterKeyword < pdf.Length && pdf[afterKeyword] == '\n')
            afterKeyword++;
        return afterKeyword < pdf.Length - 2 ? afterKeyword : -1;
    }

    private static bool IsTextFlateStream(byte[] pdf, int keyword)
    {
        var from = Math.Max(0, keyword - DictionaryLookBack);
        var window = pdf.AsSpan(from, keyword - from);
        var header = window.LastIndexOf("obj"u8);
        var dictionary = header >= 0 ? window[header..] : window;
        return dictionary.IndexOf("/Fl"u8) >= 0 && dictionary.IndexOf("/Image"u8) < 0;
    }

    private static bool HasZlibHeader(byte[] pdf, int start) =>
        (pdf[start] & 0x0F) == 8 && ((pdf[start] << 8) | pdf[start + 1]) % 31 == 0;

    private static long InflatedLength(byte[] pdf, int start, long stopAfter, byte[] buffer)
    {
        long length = 0;
        try
        {
            using var inflater = new ZLibStream(new MemoryStream(pdf, start, pdf.Length - start, writable: false), CompressionMode.Decompress);
            int read;
            while (length < stopAfter && (read = inflater.Read(buffer, 0, buffer.Length)) > 0)
                length += read;
        }
        catch (InvalidDataException)
        {
        }

        return length;
    }
}
