using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;

namespace Geef.Atelier.Infrastructure.Knowledge;

internal sealed class PdfTextExtractor(ILogger<PdfTextExtractor> logger)
{
    public PdfExtractionResult ExtractText(byte[] pdfBytes)
    {
        try
        {
            using var document = PdfDocument.Open(pdfBytes);
            if (document.IsEncrypted)
                return PdfExtractionResult.Failed("PDF is password-protected and cannot be read. Please provide an unlocked PDF.");

            var pageTexts = new List<string>();
            foreach (var page in document.GetPages())
            {
                var text = page.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(text))
                    pageTexts.Add(text);
            }

            if (pageTexts.Count == 0)
                return PdfExtractionResult.Failed("This PDF appears to contain no extractable text. Scanned image-only PDFs are not supported.");

            return PdfExtractionResult.Success(string.Join("\n\n", pageTexts), document.NumberOfPages);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to extract text from PDF");
            return PdfExtractionResult.Failed($"PDF parsing failed: {ex.Message}");
        }
    }
}

internal sealed record PdfExtractionResult(bool IsSuccess, string? Text, int? PageCount, string? ErrorMessage)
{
    public static PdfExtractionResult Success(string text, int pageCount) => new(true, text, pageCount, null);
    public static PdfExtractionResult Failed(string error) => new(false, null, null, error);
}
