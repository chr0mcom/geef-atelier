using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

/// <summary>
/// Wraps <see cref="FakeAnthropicClient"/> with a <see cref="SemaphoreSlim"/> gate that pauses
/// all API calls until the test releases the gate. Used in concurrency tests to control when runs
/// proceed, enabling deterministic mid-flight state assertions.
/// </summary>
internal sealed class GatedFakeAnthropicClient(SemaphoreSlim gate) : IAnthropicClient
{
    private readonly FakeAnthropicClient _inner = new();

    /// <inheritdoc/>
    public async Task<AnthropicResponse> CompleteAsync(AnthropicRequest req, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await _inner.CompleteAsync(req, ct); }
        finally { gate.Release(); }
    }
}
