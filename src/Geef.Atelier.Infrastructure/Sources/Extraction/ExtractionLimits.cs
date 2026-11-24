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
    long MaxPdfInflatedBytes)
{
    /// <summary>JSON format of the limits and of the extraction result.</summary>
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
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
        options.MaxPdfInflatedBytes);

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
    };
}
