using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Core.Configuration;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>
/// The limits the extraction process needs, handed over as JSON in <see cref="SourceExtractionWorker.LimitsVariable"/>.
/// Contains no paths, connection strings or secrets.
/// </summary>
internal sealed record ExtractionLimits(
    int MaxPages,
    long ExtractionTimeoutMs,
    long PageLayoutTimeoutMs,
    int ScannedPageCharThreshold,
    int MaxArchiveEntries,
    long MaxUncompressedBytes,
    int MaxCompressionRatio,
    int MaxXmlDepth,
    long MaxPdfInflatedBytes,
    int MaxExtractedChars)
{
    /// <summary>
    /// JSON format of the limits and of the extraction result. Letters of the basic plane are written without escaping;
    /// characters outside it and format or control characters are written as <c>\uXXXX</c>, so one UTF-16 unit takes at
    /// most six bytes and the answer stays within <see cref="SourceOptions.ExtractionMaxOutputBytes"/>. The JSON only
    /// travels through the private pipe between the two processes.
    /// </summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Takes the limits from <paramref name="options"/>; the extraction itself gets <paramref name="timeout"/>.</summary>
    public static ExtractionLimits From(SourceOptions options, TimeSpan timeout) => new(
        options.MaxPages,
        (long)timeout.TotalMilliseconds,
        (long)options.PageLayoutTimeout.TotalMilliseconds,
        options.ScannedPageCharThreshold,
        options.MaxArchiveEntries,
        options.MaxUncompressedBytes,
        options.MaxCompressionRatio,
        options.MaxXmlDepth,
        options.MaxPdfInflatedBytes,
        options.MaxExtractedChars);

    /// <summary>Builds the options the extractors read.</summary>
    public SourceOptions ToOptions() => new()
    {
        MaxPages = MaxPages,
        ExtractionTimeout = TimeSpan.FromMilliseconds(ExtractionTimeoutMs),
        PageLayoutTimeout = TimeSpan.FromMilliseconds(PageLayoutTimeoutMs),
        ScannedPageCharThreshold = ScannedPageCharThreshold,
        MaxArchiveEntries = MaxArchiveEntries,
        MaxUncompressedBytes = MaxUncompressedBytes,
        MaxCompressionRatio = MaxCompressionRatio,
        MaxXmlDepth = MaxXmlDepth,
        MaxPdfInflatedBytes = MaxPdfInflatedBytes,
        MaxExtractedChars = MaxExtractedChars,
    };
}
