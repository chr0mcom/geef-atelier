namespace Geef.Atelier.Core.Configuration;

/// <summary>Configuration for the BackgroundService that polls and dispatches Pending runs.</summary>
public sealed class OrchestratorOptions
{
    /// <summary>How often the orchestrator polls the database for new Pending runs.</summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Maximum number of pipeline runs that may execute concurrently.</summary>
    public int MaxConcurrentRuns { get; set; } = 5;
}
