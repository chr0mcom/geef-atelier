namespace Geef.Atelier.Core.Configuration;

/// <summary>Limits and storage of uploaded sources (section <c>Sources</c>).</summary>
public sealed class SourceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Sources";

    /// <summary>Directory that holds the original files.</summary>
    public string StorageRoot { get; set; } = "/app/sources";

    /// <summary>Most files one order may use.</summary>
    public int MaxFilesPerOrder { get; set; } = 10;

    /// <summary>Largest accepted file, in bytes (25 MiB).</summary>
    public long MaxFileBytes { get; set; } = 26_214_400;

    /// <summary>Most pages a PDF may have.</summary>
    public int MaxPages { get; set; } = 500;

    /// <summary>Total characters of an order that go into the prompt in full text without a document map (inclusive).</summary>
    public int FullTextMaxChars { get; set; } = 150_000;

    /// <summary>Total characters of an order that are still accepted with a document map (inclusive); above, the order is refused.</summary>
    public int MapMaxChars { get; set; } = 600_000;

    /// <summary>Measured characters per model token of extracted text, used for token estimates.</summary>
    public double CharsPerToken { get; set; } = 1.9;

    /// <summary>Longest line of extracted text; longer lines are wrapped.</summary>
    public int MaxLineChars { get; set; } = 1_000;

    /// <summary>Characters shown as the preview of a receipt.</summary>
    public int PreviewChars { get; set; } = 800;

    /// <summary>Average characters per page below which a PDF counts as likely scanned.</summary>
    public int ScannedPageCharThreshold { get; set; } = 200;

    /// <summary>How long a source that was never attached to an order is kept.</summary>
    public TimeSpan UnattachedRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Longest time the text extraction of one file may take; an isolated extraction is killed after this time.</summary>
    public TimeSpan ExtractionTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Where PDF and Word files are read: in a separate, limited process (default) or inside the web process (only for tests).</summary>
    public SourceExtractionMode ExtractionMode { get; set; } = SourceExtractionMode.Isolated;

    /// <summary>Managed heap limit of the extraction process in bytes; the process is also killed when its memory grows far beyond it.</summary>
    public long ExtractionMemoryLimitBytes { get; set; } = 1_073_741_824;

    /// <summary>Most PDF or Word files read at the same time across all users.</summary>
    public int ExtractionMaxConcurrency { get; set; } = 2;

    /// <summary>Most uploads processed at the same time across all users.</summary>
    public int MaxConcurrentIntakes { get; set; } = 8;

    /// <summary>How long an upload waits for a free slot before it is refused as busy.</summary>
    public TimeSpan ExtractionQueueTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Largest answer of the extraction process in bytes; a larger answer counts as unreadable.</summary>
    public long ExtractionMaxOutputBytes { get; set; } = 67_108_864;

    /// <summary>Path of the application assembly that runs the extraction process; set by the web host when empty.</summary>
    public string? ExtractionWorkerAssembly { get; set; }

    /// <summary>Path of the <c>dotnet</c> host that starts the extraction process; found automatically when empty.</summary>
    public string? ExtractionDotnetPath { get; set; }

    /// <summary>Most sources a user may keep that are not attached to an order.</summary>
    public int MaxUnattachedSourcesPerUser { get; set; } = 50;

    /// <summary>Largest total size in bytes of the sources a user keeps that are not attached to an order (500 MB).</summary>
    public long MaxUnattachedBytesPerUser { get; set; } = 524_288_000;

    /// <summary>Age after which files in the sources directory that no source refers to are deleted.</summary>
    public TimeSpan OrphanFileAge { get; set; } = TimeSpan.FromHours(24);

    /// <summary>How often expired unattached sources are deleted.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>How long the original file of a source is kept after the last order using it finished; the extracted text stays.</summary>
    public TimeSpan OriginalRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Longest time the layout analysis of one PDF page may take before the plain text order is used instead.</summary>
    public TimeSpan PageLayoutTimeout { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Most entries a Word file (a ZIP archive) may contain.</summary>
    public int MaxArchiveEntries { get; set; } = 10_000;

    /// <summary>Largest total uncompressed size of a Word file, in bytes (64 MiB).</summary>
    public long MaxUncompressedBytes { get; set; } = 67_108_864;

    /// <summary>Highest ratio of uncompressed to compressed size of one entry of a Word file.</summary>
    public int MaxCompressionRatio { get; set; } = 200;

    /// <summary>Deepest element nesting accepted in every XML part of a Word file.</summary>
    public int MaxXmlDepth { get; set; } = 100;

    /// <summary>Largest total size in bytes that the compressed text streams of a PDF may unpack to (256 MiB); pictures are not counted.</summary>
    public long MaxPdfInflatedBytes { get; set; } = 268_435_456;
}

/// <summary>Where PDF and Word files are read.</summary>
public enum SourceExtractionMode
{
    /// <summary>In a separate process with time, memory and output limits.</summary>
    Isolated = 0,

    /// <summary>Inside the calling process; only for tests of the extractors.</summary>
    InProcess = 1,
}
