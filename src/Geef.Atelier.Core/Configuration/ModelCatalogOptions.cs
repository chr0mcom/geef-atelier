namespace Geef.Atelier.Core.Configuration;

/// <summary>Cache, timeout and warm-up settings for the model catalog. Bound from "ModelCatalog".</summary>
public sealed class ModelCatalogOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ModelCatalog";

    /// <summary>Cache lifetime for a successful CLI-provider lookup. Mirrors the cli-proxy's own cache.</summary>
    public int CliTtlHours { get; set; } = 24;

    /// <summary>Cache lifetime for a successful HTTP-provider lookup.</summary>
    public int HttpTtlHours { get; set; } = 1;

    /// <summary>
    /// Cache lifetime for a failed lookup. Deliberately short: without it a single unlucky attempt
    /// would freeze the stale fallback list for a whole day.
    /// </summary>
    public int FailureTtlMinutes { get; set; } = 5;

    /// <summary>Per-attempt budget for interactive calls, so the picker never waits on a cold backend.</summary>
    public int InteractiveTimeoutSeconds { get; set; } = 5;

    /// <summary>
    /// Per-attempt budget for a user-triggered refresh. Larger than the interactive budget because a
    /// refresh deliberately asks the gateway to re-resolve instead of serving its cache, and that
    /// costs real CLI probes; smaller than the warm-up budget because someone is waiting.
    /// </summary>
    public int RefreshTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Per-attempt budget for the nightly warm-up. Must stay clear of the gateway's own probe
    /// timeout plus its queueing time, or the sweep times out exactly when it has work to do.
    /// </summary>
    public int WarmUpTimeoutSeconds { get; set; } = 240;

    /// <summary>Enables the nightly warm-up background service.</summary>
    public bool WarmUpEnabled { get; set; } = true;

    /// <summary>Hour (UTC) of the nightly warm-up.</summary>
    public int WarmUpHourUtc { get; set; } = 3;

    /// <summary>Minute (UTC) of the nightly warm-up.</summary>
    public int WarmUpMinuteUtc { get; set; } = 15;

    /// <summary>Delay before the start-up warm-up, so schema migration can finish first.</summary>
    public int WarmUpStartupDelaySeconds { get; set; } = 60;

    /// <summary>
    /// Kill switch for the live CLI catalog. When false, CLI providers serve their configured
    /// model list without contacting the gateway — a way back that needs no redeploy.
    /// </summary>
    public bool CliLiveCatalogEnabled { get; set; } = true;
}
