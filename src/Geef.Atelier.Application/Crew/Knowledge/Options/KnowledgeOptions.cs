namespace Geef.Atelier.Application.Crew.Knowledge.Options;

/// <summary>
/// Configuration for knowledge-base document upload validation.
/// Bound from <c>Knowledge</c> in application settings.
/// </summary>
public sealed class KnowledgeOptions
{
    /// <summary>Maximum permitted document size in bytes (default: 5 MB).</summary>
    public long MaxDocumentSizeBytes { get; set; } = 5_242_880;

    /// <summary>MIME types accepted for upload (default: <c>text/markdown</c> and <c>text/plain</c>).</summary>
    public IReadOnlyList<string> AllowedContentTypes { get; set; } = ["text/markdown", "text/plain"];
}
