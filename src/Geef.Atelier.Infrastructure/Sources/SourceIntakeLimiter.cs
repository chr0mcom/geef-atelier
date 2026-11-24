using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>
/// Process-wide limits of the source intake: at most <see cref="SourceOptions.MaxConcurrentIntakes"/> uploads and at most
/// <see cref="SourceOptions.ExtractionMaxConcurrency"/> PDF or Word extractions at the same time. A caller waits up to
/// <see cref="SourceOptions.ExtractionQueueTimeout"/> for a free slot.
/// </summary>
internal sealed class SourceIntakeLimiter(IOptions<SourceOptions> options) : IDisposable
{
    internal const string BusyReason = "The service is busy with other uploads. Please try again in a minute.";

    private readonly SemaphoreSlim _intakes = new(Math.Max(1, options.Value.MaxConcurrentIntakes));
    private readonly SemaphoreSlim _extractions = new(Math.Max(1, options.Value.ExtractionMaxConcurrency));

    /// <summary>Waits for an upload slot; <c>null</c> when none became free in time.</summary>
    public Task<IDisposable?> EnterIntakeAsync(CancellationToken ct) => EnterAsync(_intakes, ct);

    /// <summary>Waits for an extraction slot; <c>null</c> when none became free in time.</summary>
    public Task<IDisposable?> EnterExtractionAsync(CancellationToken ct) => EnterAsync(_extractions, ct);

    public void Dispose()
    {
        _intakes.Dispose();
        _extractions.Dispose();
    }

    private async Task<IDisposable?> EnterAsync(SemaphoreSlim gate, CancellationToken ct)
    {
        var wait = options.Value.ExtractionQueueTimeout;
        return await gate.WaitAsync(wait < TimeSpan.Zero ? TimeSpan.Zero : wait, ct) ? new Slot(gate) : null;
    }

    private sealed class Slot(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }
}
