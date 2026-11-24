using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Sources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources.Extraction;

/// <summary>Reads the text of a received file.</summary>
internal interface ISourceExtractionRunner
{
    /// <summary>
    /// Reads <paramref name="inputPath"/> as <paramref name="kind"/>; <paramref name="workDirectory"/> is a private
    /// temporary directory for the extraction. Never throws for broken files; answers with an unreadable result instead.
    /// </summary>
    Task<SourceExtraction> ExtractAsync(SourceKind kind, string inputPath, string workDirectory, CancellationToken ct);
}

/// <summary>
/// Text and Markdown are read in the web process. PDF and Word files take an extraction slot of
/// <see cref="SourceIntakeLimiter"/> and are read in a separate process (<see cref="SourceExtractionMode.Isolated"/>)
/// or, only for tests, in the web process with a time limit (<see cref="SourceExtractionMode.InProcess"/>).
/// </summary>
internal sealed class SourceExtractionRunner(
    IEnumerable<ISourceTextExtractor> extractors,
    IsolatedExtractionProcess isolated,
    SourceIntakeLimiter limiter,
    IOptions<SourceOptions> options,
    ILogger<SourceExtractionRunner> logger) : ISourceExtractionRunner
{
    public async Task<SourceExtraction> ExtractAsync(SourceKind kind, string inputPath, string workDirectory, CancellationToken ct)
    {
        if (kind is not (SourceKind.Pdf or SourceKind.Docx))
            return await Extractor(SourceKind.Text).ExtractAsync(await File.ReadAllBytesAsync(inputPath, ct), ct);

        using var slot = await limiter.EnterExtractionAsync(ct);
        if (slot is null)
        {
            logger.LogWarning("A {Kind} file was refused because all extraction slots stayed busy.", kind);
            return SourceExtraction.Unreadable(SourceIntakeLimiter.BusyReason);
        }

        var opts = options.Value;
        if (opts.ExtractionMode == SourceExtractionMode.InProcess)
            return await ExtractInProcessAsync(kind, inputPath, opts, ct);

        var result = await isolated.RunAsync(kind, inputPath, workDirectory, ct);
        if (result.Outcome == IsolatedExtractionOutcome.Completed)
            logger.LogInformation("Read a {Kind} file in {Elapsed} ms (peak memory {PeakMb} MB).", kind, (long)result.Elapsed.TotalMilliseconds, result.PeakMemoryBytes / 1_048_576);
        else
            logger.LogWarning("Reading a {Kind} file ended with {Outcome} after {Elapsed} ms (exit code {ExitCode}, peak memory {PeakMb} MB).",
                kind, result.Outcome, (long)result.Elapsed.TotalMilliseconds, result.ExitCode, result.PeakMemoryBytes / 1_048_576);
        return result.Extraction;
    }

    private async Task<SourceExtraction> ExtractInProcessAsync(SourceKind kind, string inputPath, SourceOptions opts, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(inputPath, ct);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(opts.ExtractionTimeout);
        try
        {
            return await Extractor(kind).ExtractAsync(bytes, limit.Token).WaitAsync(opts.ExtractionTimeout, ct);
        }
        catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            logger.LogWarning("Extraction of a {Kind} file took longer than {Timeout}.", kind, opts.ExtractionTimeout);
            return SourceExtraction.Unreadable(IsolatedExtractionProcess.TooSlowReason);
        }
    }

    private ISourceTextExtractor Extractor(SourceKind kind) =>
        extractors.FirstOrDefault(e => e.Kind == kind) ?? extractors.First(e => e.Kind == SourceKind.Text);
}
