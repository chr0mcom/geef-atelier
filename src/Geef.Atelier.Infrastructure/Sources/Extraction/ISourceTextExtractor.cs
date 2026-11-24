using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>Result of reading one file: the text (with page markers for PDFs), empty pages, quality, warnings, or why it could not be read.</summary>
internal sealed record SourceExtraction(string? Text, int? PageCount, IReadOnlyList<int> EmptyPages, SourceQuality Quality, IReadOnlyList<SourceWarning> Warnings, string? FailureReason)
{
    /// <summary>Reason for a file with more text than <c>SourceOptions.MaxExtractedChars</c>.</summary>
    public const string TooMuchTextReason = "This file contains far more text than we can use. Please upload a shorter version.";

    /// <summary>An unreadable file because it holds more text than one source may have.</summary>
    public static SourceExtraction TooMuchText(int? pageCount = null) => Unreadable(TooMuchTextReason, pageCount: pageCount);

    /// <summary>An unreadable file with a plain-English reason.</summary>
    public static SourceExtraction Unreadable(string reason, IReadOnlyList<SourceWarning>? warnings = null, int? pageCount = null) =>
        new(null, pageCount, [], SourceQuality.Unreadable, warnings ?? [], reason);
}

/// <summary>Reads the text of one kind of file.</summary>
internal interface ISourceTextExtractor
{
    /// <summary>The kind of file this extractor reads.</summary>
    SourceKind Kind { get; }

    /// <summary>Reads <paramref name="content"/>; never throws for broken files, it answers with <see cref="SourceQuality.Unreadable"/> instead.</summary>
    Task<SourceExtraction> ExtractAsync(ReadOnlyMemory<byte> content, CancellationToken ct);
}
