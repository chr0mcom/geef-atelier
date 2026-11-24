using System.Collections.Concurrent;

namespace Geef.Atelier.Core.Domain.Warnings;

/// <summary>
/// Ambient warning context of the run that is executing on the current async flow. The orchestrator opens it once
/// per run; code deep inside the pipeline that has no run id (for example the LLM HTTP client) reports through
/// <see cref="Current"/>. The context flows into awaited calls and child tasks, never back to the caller and never
/// into other runs.
/// </summary>
public sealed class RunWarningScope
{
    private static readonly AsyncLocal<RunWarningScope?> Ambient = new();

    private readonly ConcurrentDictionary<string, byte> _reported = new(StringComparer.Ordinal);

    private RunWarningScope(Guid runId, IRunWarningSink sink)
    {
        RunId = runId;
        Sink = sink;
    }

    /// <summary>The scope of the run executing on this async flow, or <c>null</c> outside a run.</summary>
    public static RunWarningScope? Current => Ambient.Value;

    /// <summary>Run the scope belongs to.</summary>
    public Guid RunId { get; }

    /// <summary>Sink the scope writes to.</summary>
    public IRunWarningSink Sink { get; }

    /// <summary>Opens the scope for <paramref name="runId"/>; disposing the handle restores the previous scope.</summary>
    public static IDisposable Begin(Guid runId, IRunWarningSink sink)
    {
        var previous = Ambient.Value;
        Ambient.Value = new RunWarningScope(runId, sink);
        return new Restore(previous);
    }

    /// <summary>
    /// Reports a warning unless one with the same <paramref name="dedupKey"/> was already reported in this scope.
    /// Returns <c>true</c> when the warning was handed to the sink.
    /// </summary>
    public async Task<bool> ReportOnceAsync(string dedupKey, string source, string code, string message, int? iteration = null, CancellationToken ct = default)
    {
        if (!_reported.TryAdd(dedupKey, 0))
            return false;
        await Sink.ReportAsync(RunId, source, code, message, iteration, ct);
        return true;
    }

    private sealed class Restore(RunWarningScope? previous) : IDisposable
    {
        public void Dispose() => Ambient.Value = previous;
    }
}
