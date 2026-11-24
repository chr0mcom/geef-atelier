using System.Diagnostics;
using System.Globalization;
using System.Text;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.PageSegmenter;
using UglyToad.PdfPig.DocumentLayoutAnalysis.ReadingOrderDetector;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
using UglyToad.PdfPig.Exceptions;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>
/// Reads PDFs page by page with layout analysis (words, Docstrum blocks, unsupervised reading order), so columns and
/// paragraphs come out in reading order. A page whose analysis fails or takes longer than
/// <see cref="SourceOptions.PageLayoutTimeout"/> falls back to the plain content order; after half of
/// <see cref="SourceOptions.ExtractionTimeout"/> or three slow pages every further page uses the plain order.
/// </summary>
internal sealed class PdfSourceExtractor(IOptions<SourceOptions> options, ILogger<PdfSourceExtractor> logger) : ISourceTextExtractor
{
    internal const string NoTextReason = "This PDF has no readable text. It may be a scan; scanned files are not supported yet.";
    internal const string EncryptedReason = "This PDF is protected with a password. Please upload a copy without a password.";
    internal const string BrokenReason = "This file could not be read as a PDF.";
    internal const string TooSlowReason = "Reading this PDF took too long.";
    private const int MaxSlowPages = 3;
    private const int UnusableAverageChars = 20;

    public SourceKind Kind => SourceKind.Pdf;

    public Task<SourceExtraction> ExtractAsync(ReadOnlyMemory<byte> content, CancellationToken ct) =>
        Task.Run(() => Extract(content.ToArray(), ct), ct);

    internal PdfExtractionStats? LastStats { get; private set; }

    private SourceExtraction Extract(byte[] bytes, CancellationToken ct)
    {
        var opts = options.Value;
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(bytes);
        }
        catch (PdfDocumentEncryptedException)
        {
            return SourceExtraction.Unreadable(EncryptedReason, [new SourceWarning(SourceWarningCodes.Encrypted, "The PDF is protected with a password.")]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogInformation("PDF could not be opened: {Error}", ex.GetType().Name);
            return SourceExtraction.Unreadable(BrokenReason);
        }

        using (document)
        {
            int pageCount;
            try
            {
                pageCount = document.NumberOfPages;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogInformation("PDF page count could not be read: {Error}", ex.GetType().Name);
                return SourceExtraction.Unreadable(BrokenReason);
            }

            if (pageCount > opts.MaxPages)
                return SourceExtraction.Unreadable(
                    string.Create(CultureInfo.InvariantCulture, $"This PDF has {pageCount:N0} pages; at most {opts.MaxPages:N0} are possible. Please split it or remove pages."),
                    [new SourceWarning(SourceWarningCodes.TooManyPages, string.Create(CultureInfo.InvariantCulture, $"The PDF has more than {opts.MaxPages:N0} pages."))],
                    pageCount);

            return ExtractPages(document, pageCount, opts, ct);
        }
    }

    private SourceExtraction ExtractPages(PdfDocument document, int pageCount, SourceOptions opts, CancellationToken ct)
    {
        var total = Stopwatch.StartNew();
        var layoutBudget = opts.ExtractionTimeout / 2;
        var builder = new StringBuilder();
        var emptyPages = new List<int>();
        var stats = new PdfExtractionStats();
        var slowPages = 0;
        var chars = 0L;

        for (var number = 1; number <= pageCount; number++)
        {
            ct.ThrowIfCancellationRequested();
            if (total.Elapsed > opts.ExtractionTimeout)
                return SourceExtraction.Unreadable(TooSlowReason, pageCount: pageCount);

            string text;
            try
            {
                var page = document.GetPage(number);
                var useLayout = slowPages < MaxSlowPages && total.Elapsed < layoutBudget;
                text = useLayout ? ReadWithLayout(page, opts.PageLayoutTimeout, stats, ref slowPages) : ReadPlain(page, stats);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogInformation("PDF page {Page} could not be read: {Error}", number, ex.GetType().Name);
                stats.FailedPages++;
                text = string.Empty;
            }

            text = Clean(text);
            if (text.Length == 0)
                emptyPages.Add(number);
            chars += text.Length;

            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append(PageMarkers.For(number));
            if (text.Length > 0)
                builder.Append('\n').Append(text);
        }

        stats.Elapsed = total.Elapsed;
        LastStats = stats;

        var average = pageCount == 0 ? 0 : chars / (double)pageCount;
        if (chars == 0 || average < UnusableAverageChars)
            return SourceExtraction.Unreadable(NoTextReason,
                [new SourceWarning(SourceWarningCodes.LikelyScanned, "The PDF looks like a scan: it has almost no text.")], pageCount);

        var warnings = new List<SourceWarning>();
        var quality = SourceQuality.Good;
        if (emptyPages.Count > 0)
        {
            quality = SourceQuality.Partial;
            warnings.Add(new SourceWarning(SourceWarningCodes.PagesWithoutText, PagesWithoutTextMessage(emptyPages), emptyPages));
        }

        if (average < opts.ScannedPageCharThreshold)
        {
            quality = SourceQuality.Poor;
            warnings.Add(new SourceWarning(SourceWarningCodes.LikelyScanned,
                "Most pages have very little text. The PDF may be a scan, so parts of it may be missing."));
        }

        return new SourceExtraction(builder.ToString(), pageCount, emptyPages, quality, warnings, null);
    }

    private static string ReadWithLayout(Page page, TimeSpan timeout, PdfExtractionStats stats, ref int slowPages)
    {
        var layout = Task.Run(() => LayoutText(page));
        try
        {
            if (layout.Wait(timeout))
            {
                stats.LayoutPages++;
                return layout.Result;
            }

            slowPages++;
            stats.TimedOutPages++;
        }
        catch (AggregateException)
        {
            stats.FallbackPages++;
        }

        return ReadPlain(page, stats);
    }

    private static string ReadPlain(Page page, PdfExtractionStats stats)
    {
        stats.PlainPages++;
        return ContentOrderTextExtractor.GetText(page);
    }

    internal static string LayoutText(Page page)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance).ToList();
        if (words.Count == 0)
            return string.Empty;

        var blocks = DocstrumBoundingBoxes.Instance.GetBlocks(words);
        var ordered = UnsupervisedReadingOrderDetector.Instance.Get(blocks);
        return string.Join("\n\n", ordered.Select(b => JoinLines(b.TextLines.Select(l => l.Text))).Where(t => t.Length > 0));
    }

    internal static string JoinLines(IEnumerable<string> lines)
    {
        var builder = new StringBuilder();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;
            if (builder.Length > 0 && builder[^1] != '-')
                builder.Append(' ');
            builder.Append(line);
        }

        return builder.ToString();
    }

    private static string Clean(string text)
    {
        var lines = text.Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.TrimEnd());
        var builder = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            if (line.Trim().Length == 0)
            {
                blank++;
                continue;
            }

            if (builder.Length > 0)
                builder.Append(blank > 0 ? "\n\n" : "\n");
            blank = 0;
            builder.Append(PageMarkers.TryParseMarker(line.Trim(), out _) ? line.Replace("[[", "[ [", StringComparison.Ordinal) : line);
        }

        return builder.ToString();
    }

    private static string PagesWithoutTextMessage(IReadOnlyList<int> pages)
    {
        var shown = string.Join(", ", pages.Take(10));
        var more = pages.Count > 10 ? string.Create(CultureInfo.InvariantCulture, $" and {pages.Count - 10} more") : string.Empty;
        return pages.Count == 1
            ? $"Page {shown} has no readable text."
            : $"Pages {shown}{more} have no readable text.";
    }
}

/// <summary>How the pages of the last PDF were read (for measurements and tests).</summary>
internal sealed class PdfExtractionStats
{
    public int LayoutPages { get; set; }
    public int PlainPages { get; set; }
    public int TimedOutPages { get; set; }
    public int FallbackPages { get; set; }
    public int FailedPages { get; set; }
    public TimeSpan Elapsed { get; set; }
}
