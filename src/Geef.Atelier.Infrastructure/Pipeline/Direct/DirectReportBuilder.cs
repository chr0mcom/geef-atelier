using Geef.Atelier.Core.Domain.Checks;
using Geef.Atelier.Core.Domain.Reports;
using Geef.Atelier.Core.Domain.Sources;

namespace Geef.Atelier.Infrastructure.Pipeline.Direct;

/// <summary>Builds the report of a direct-path run from the delivered text, which is checked once more here.</summary>
internal static class DirectReportBuilder
{
    /// <summary>Report note when the input tokens suggest that the files were searched.</summary>
    public const string PartialReadNote = "Parts of your files were searched, not read in full.";

    /// <summary>
    /// Checks <paramref name="deliveredText"/> again (this also covers a best-effort version), lists its placeholders
    /// and the files used by the writer, and adds the read evidence of the first pass.
    /// </summary>
    public static RunReport Build(
        OrderConstraints constraints,
        string deliveredText,
        IReadOnlyList<RunSourceUsageEntity> usages,
        int? deliveredIteration,
        int iterations,
        ReadEvidence? readEvidence = null)
    {
        var checks = DraftCheckEvaluator.Evaluate(deliveredText, constraints);
        var sources = usages
            .Where(u => u.Target == OrderGroundingStep.WriterTarget)
            .OrderBy(u => u.Position)
            .Select(u => new RunReportSource(u.SourceId, u.FileName, u.Mode, u.CharsIncluded))
            .ToList();

        var notes = new List<string>();
        if (readEvidence is { ReadInFull: false })
            notes.Add(PartialReadNote);

        return new RunReport(
            RunReport.CurrentSchemaVersion,
            RunReport.DirectKind,
            constraints,
            checks,
            DraftCheckEvaluator.FindPlaceholders(deliveredText),
            sources,
            deliveredIteration,
            iterations,
            readEvidence,
            notes);
    }
}
