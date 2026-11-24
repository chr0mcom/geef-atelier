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
    public int FullTextMaxChars { get; set; } = 300_000;

    /// <summary>Total characters of an order that are still accepted with a document map (inclusive); above, the order is refused.</summary>
    public int MapMaxChars { get; set; } = 900_000;

    /// <summary>Longest line of extracted text; longer lines are wrapped.</summary>
    public int MaxLineChars { get; set; } = 1_000;

    /// <summary>Characters shown as the preview of a receipt.</summary>
    public int PreviewChars { get; set; } = 800;

    /// <summary>Average characters per page below which a PDF counts as likely scanned.</summary>
    public int ScannedPageCharThreshold { get; set; } = 200;

    /// <summary>How long a source that was never attached to an order is kept.</summary>
    public TimeSpan UnattachedRetention { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Longest time the text extraction of one file may take.</summary>
    public TimeSpan ExtractionTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How often expired unattached sources are deleted.</summary>
    public TimeSpan CleanupInterval { get; set; } = TimeSpan.FromHours(1);
}
