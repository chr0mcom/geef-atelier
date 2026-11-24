namespace Geef.Atelier.Core.Configuration;

/// <summary>Configuration for the BackgroundService that polls and dispatches Pending runs.</summary>
public sealed class OrchestratorOptions
{
    /// <summary>How often the orchestrator polls the database for new Pending runs.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Maximum number of pipeline runs that may execute concurrently.</summary>
    public int MaxConcurrentRuns { get; set; } = 5;

    /// <summary>How often the per-run cancellation watcher polls the database for the CancellationRequested flag.</summary>
    public TimeSpan CancellationPollingInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long a Pending run may stay without a saved crew snapshot (attachments still being prepared) before it
    /// is failed. A run without a snapshot is never picked up.
    /// </summary>
    public TimeSpan PreparationTimeout { get; set; } = TimeSpan.FromMinutes(15);
}
