using Geef.Atelier.Core.Domain.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>
/// Maps the sentences of the kind detection and of the text extraction to the codes of <see cref="SourceRefusalCodes"/>.
/// It lives here because the sentences are constants of the infrastructure; a sentence without a code of its own maps to
/// <see cref="SourceRefusalCodes.Other"/>.
/// </summary>
internal static class SourceRefusalCodeMap
{
    /// <summary>
    /// The code of a refusal with <paramref name="reason"/> from <see cref="SourceKindDetector"/> or an extractor;
    /// <paramref name="warnings"/> tell refusals apart whose sentence holds numbers (too many pages).
    /// </summary>
    public static string FromExtractionReason(string? reason, IReadOnlyList<SourceWarning>? warnings = null)
    {
        if (warnings is not null && warnings.Any(w => w.Code == SourceWarningCodes.TooManyPages))
            return SourceRefusalCodes.TooManyPages;

        return reason switch
        {
            SourceKindDetector.UnsupportedReason => SourceRefusalCodes.UnsupportedType,
            SourceKindDetector.OldWordReason => SourceRefusalCodes.OldWord,
            DocxSourceExtractor.ArchiveReason => SourceRefusalCodes.ArchiveRefused,
            DocxSourceExtractor.NestingReason or PdfSourceExtractor.InflatedReason or IsolatedExtractionProcess.TooComplexReason
                => SourceRefusalCodes.TooComplex,
            DocxSourceExtractor.BrokenReason or PdfSourceExtractor.BrokenReason or IsolatedExtractionProcess.FailedReason
                => SourceRefusalCodes.Unreadable,
            DocxSourceExtractor.NoTextReason or PlainTextSourceExtractor.EmptyReason or SourceIntakeService.EmptyReason
                or SourceIntakeService.NoReadableTextReason => SourceRefusalCodes.Empty,
            PdfSourceExtractor.NoTextReason => SourceRefusalCodes.PdfNoText,
            PdfSourceExtractor.EncryptedReason => SourceRefusalCodes.PdfEncrypted,
            PdfSourceExtractor.TooSlowReason or IsolatedExtractionProcess.TooSlowReason => SourceRefusalCodes.Timeout,
            SourceIntakeLimiter.BusyReason => SourceRefusalCodes.Busy,
            IsolatedExtractionProcess.UnavailableReason => SourceRefusalCodes.Unavailable,
            SourceExtraction.TooMuchTextReason => SourceRefusalCodes.TooMuchText,
            SourceIntakeService.SaveFailedReason => SourceRefusalCodes.SaveFailed,
            SourceIntakeService.InterruptedReason => SourceRefusalCodes.Interrupted,
            _ => SourceRefusalCodes.Other,
        };
    }
}
