namespace Geef.Atelier.Core.Domain.Sources;

/// <summary>Kind of an uploaded file, determined from its content. New kinds are appended at the end.</summary>
public enum SourceKind { Pdf = 0, Docx = 1, Text = 2, Markdown = 3 }

/// <summary>How well the text of a file could be read. <see cref="Unreadable"/> is never stored.</summary>
public enum SourceQuality { Good = 0, Partial = 1, Poor = 2, Unreadable = 3 }

/// <summary>How a file reached the service.</summary>
public enum SourceOrigin { WebUpload = 0, McpAttachment = 1, Pasted = 2 }

/// <summary>What a file is for within an order.</summary>
public enum SourceRole { Facts = 0, Background = 1, StyleReference = 2, TemplateToRevise = 3 }

/// <summary>How the text of a file went into a prompt.</summary>
public enum SourceUsageMode { FullText = 0, FullTextWithMap = 1, Excluded = 2 }

/// <summary>Size class of the files of an order measured against the character budget.</summary>
public enum SourceBudgetClass { FullText = 0, WithMap = 1, TooLarge = 2 }

/// <summary>A problem found while reading a file, with a plain-English message and optionally the affected pages.</summary>
public sealed record SourceWarning(string Code, string Message, IReadOnlyList<int>? Pages = null);

/// <summary>Known codes of <see cref="SourceWarning"/>.</summary>
public static class SourceWarningCodes
{
    public const string PagesWithoutText = "pdf.pages_without_text";
    public const string LikelyScanned = "pdf.likely_scanned";
    public const string Encrypted = "pdf.encrypted";
    public const string EncodingGuessed = "text.encoding_guessed";
    public const string ReplacementCharacters = "text.replacement_characters";
    public const string LongLinesWrapped = "text.long_lines_wrapped";
    public const string DocxContentSkipped = "docx.content_skipped";
    public const string UsesDocumentMap = "budget.document_map";
    public const string OverBudget = "budget.too_large";
    public const string TooManyPages = "limit.too_many_pages";
    public const string Duplicate = "source.duplicate";
}
