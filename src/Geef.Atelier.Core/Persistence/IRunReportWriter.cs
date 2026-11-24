namespace Geef.Atelier.Core.Persistence;

/// <summary>Stores the structured report of a run in <c>Runs.ReportJson</c>. Uses its own short-lived scope; never throws except for cancellation.</summary>
public interface IRunReportWriter
{
    /// <summary>Writes <paramref name="reportJson"/> to the run <paramref name="runId"/>.</summary>
    Task WriteAsync(Guid runId, string reportJson, CancellationToken ct = default);
}
