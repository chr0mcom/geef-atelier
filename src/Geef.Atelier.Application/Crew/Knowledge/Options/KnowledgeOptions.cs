namespace Geef.Atelier.Application.Crew.Knowledge.Options;

/// <summary>
/// Configuration for knowledge-base document upload validation.
/// Bound from <c>Knowledge</c> in application settings.
/// </summary>
public sealed class KnowledgeOptions
{
    /// <summary>Maximum permitted document size in bytes for text files (default: 5 MB).</summary>
    public long MaxDocumentSizeBytes { get; set; } = 5_242_880;

    /// <summary>Maximum permitted document size in bytes for PDF files (default: 25 MB).</summary>
    public long MaxPdfSizeBytes { get; set; } = 26_214_400;

    /// <summary>MIME types accepted for upload.</summary>
    public IReadOnlyList<string> AllowedContentTypes { get; set; } = ["text/markdown", "text/plain", "application/pdf"];
}
