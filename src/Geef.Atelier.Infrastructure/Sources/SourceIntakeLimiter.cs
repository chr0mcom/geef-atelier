using System.Globalization;
using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>
/// Process-wide limits of the source intake: at most <see cref="SourceOptions.MaxConcurrentUploadsPerUser"/> uploads per
/// user (a further one is refused at once), at most <see cref="SourceOptions.MaxConcurrentIntakes"/> uploads and at most
/// <see cref="SourceOptions.ExtractionMaxConcurrency"/> PDF or Word extractions at the same time. A caller waits up to
/// <see cref="SourceOptions.ExtractionQueueTimeout"/> for a free upload or extraction slot.
/// </summary>
internal sealed class SourceIntakeLimiter(IOptions<SourceOptions> options) : IDisposable
{
    internal const string BusyReason = "The service is busy with other uploads. Please try again in a minute.";

    private readonly SemaphoreSlim _intakes = new(Math.Max(1, options.Value.MaxConcurrentIntakes));
    private readonly SemaphoreSlim _extractions = new(Math.Max(1, options.Value.ExtractionMaxConcurrency));
    private readonly Dictionary<string, int> _perUser = new(StringComparer.Ordinal);

    /// <summary>Most uploads one user may run at the same time.</summary>
    public int PerUserLimit { get; } = Math.Max(1, options.Value.MaxConcurrentUploadsPerUser);

    /// <summary>Reason for an upload refused because the user already runs <see cref="PerUserLimit"/> uploads.</summary>
    public string UserBusyReason => PerUserLimit == 1
        ? "You are already adding a file. Please wait until it is done, then try again."
        : string.Create(CultureInfo.InvariantCulture, $"You are already adding {PerUserLimit} files at the same time. Please wait until one is done, then try again.");

    /// <summary>Takes one of the upload places of <paramref name="owner"/> without waiting; <c>null</c> when all are taken.</summary>
    public IDisposable? TryEnterUser(string owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_perUser)
        {
            var running = _perUser.GetValueOrDefault(owner);
            if (running >= PerUserLimit)
                return null;
            _perUser[owner] = running + 1;
        }

        return new Slot(() => LeaveUser(owner));
    }

    /// <summary>Waits for an upload slot; <c>null</c> when none became free in time.</summary>
    public Task<IDisposable?> EnterIntakeAsync(CancellationToken ct) => EnterAsync(_intakes, ct);

    /// <summary>Waits for an extraction slot; <c>null</c> when none became free in time.</summary>
    public Task<IDisposable?> EnterExtractionAsync(CancellationToken ct) => EnterAsync(_extractions, ct);

    public void Dispose()
    {
        _intakes.Dispose();
        _extractions.Dispose();
    }

    private void LeaveUser(string owner)
    {
        lock (_perUser)
        {
            var running = _perUser.GetValueOrDefault(owner) - 1;
            if (running <= 0)
                _perUser.Remove(owner);
            else
                _perUser[owner] = running;
        }
    }

    private async Task<IDisposable?> EnterAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        var wait = options.Value.ExtractionQueueTimeout;
        return await gate.WaitAsync(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, ct) ? new Slot(() => gate.Release()) : null;
    }

    private sealed class Slot(Action release) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                release();
        }
    }
}
