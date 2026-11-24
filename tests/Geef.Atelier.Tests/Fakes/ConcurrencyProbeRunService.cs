using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew.Advisors;
using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Tests.Fakes;

/// <summary>
/// <see cref="IRunService"/> that records how many calls overlap, the way a shared <c>DbContext</c> would see them.
/// Every call after the first <see cref="ImmediateCalls"/> waits for <see cref="Hold"/>; <see cref="GetRunAsync"/>
/// can be held separately through <see cref="GetRunHold"/>. With <paramref name="details"/> the detail page can load
/// (<see cref="GetRunWithGroundingAsync"/>) and resume; both count like the other data calls.
/// </summary>
internal sealed class ConcurrencyProbeRunService(RunEntity? run = null, RunDetails? details = null) : IRunService
{
    private int _active;
    private int _maxActive;
    private int _calls;

    /// <summary>Number of calls that return without waiting for <see cref="Hold"/>.</summary>
    public int ImmediateCalls { get; set; } = int.MaxValue;

    /// <summary>Released by the test to let held calls finish.</summary>
    public TaskCompletionSource Hold { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>When set, <see cref="GetRunAsync"/> waits for it (access checks of the live feed).</summary>
    public TaskCompletionSource? GetRunHold { get; set; }

    /// <summary>Highest number of calls in progress at the same time.</summary>
    public int MaxActive => Volatile.Read(ref _maxActive);

    /// <summary>Number of calls started so far.</summary>
    public int Calls => Volatile.Read(ref _calls);

    /// <summary>Number of calls in progress right now.</summary>
    public int Active => Volatile.Read(ref _active);

    public Task<IReadOnlyList<RunEntity>> ListRunsAsync(int limit = 20, RunStatus? statusFilter = null, string? requestingUsername = null, CancellationToken cancellationToken = default) =>
        TrackAsync<IReadOnlyList<RunEntity>>(run is null ? [] : [run]);

    public Task<bool> DeleteRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) =>
        TrackAsync(true);

    public Task<bool> CancelRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) =>
        TrackAsync(true);

    public async Task<RunEntity?> GetRunAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default)
    {
        if (GetRunHold is { } hold)
            await hold.Task.WaitAsync(cancellationToken);
        return run;
    }

    public Task<Guid> SubmitRunAsync(SubmitRunRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<RunWarningEntity>> ListRunWarningsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RunWarningEntity>>([]);

    public Task<RunDetails?> GetRunDetailsAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<RunWithGroundingViewModel?> GetRunWithGroundingAsync(Guid runId, string? requestingUsername, CancellationToken cancellationToken = default) =>
        details is null
            ? throw new NotSupportedException()
            : TrackAsync<RunWithGroundingViewModel?>(new RunWithGroundingViewModel(
                Details: details,
                Snapshot: null,
                GroundedBrief: details.Run.BriefingText,
                GroundingAdvisors: [],
                RecoveryAdvisors: [],
                AdvisorsByIteration: Enumerable.Empty<AdvisorConsultation>().ToLookup(x => x.IterationNumber),
                GroundingConsultations: [],
                ToolInvocations: [],
                Warnings: []));

    public Task<WelcomeStats> GetWelcomeStatsAsync(string? requestingUsername, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<Guid> ResumeRunAsync(ResumeOptions options, string? requestingUsername, CancellationToken cancellationToken = default) =>
        details is null ? throw new NotSupportedException() : TrackAsync(Guid.NewGuid());

    private async Task<T> TrackAsync<T>(T result)
    {
        var call = Interlocked.Increment(ref _calls);
        var active = Interlocked.Increment(ref _active);
        int seen;
        while (active > (seen = Volatile.Read(ref _maxActive)) && Interlocked.CompareExchange(ref _maxActive, active, seen) != seen)
        {
        }
        try
        {
            if (call > ImmediateCalls)
                await Hold.Task;
            else
                await Task.Yield();
            return result;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}
