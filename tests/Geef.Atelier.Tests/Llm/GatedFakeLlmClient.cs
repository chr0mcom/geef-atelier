using Geef.Atelier.Infrastructure.Llm;

namespace Geef.Atelier.Tests.Llm;

/// <summary>
/// Wraps <see cref="FakeLlmClient"/> with a <see cref="SemaphoreSlim"/> gate that pauses
/// all API calls until the test releases the gate. Used in concurrency tests to control when runs
/// proceed, enabling deterministic mid-flight state assertions.
/// </summary>
internal sealed class GatedFakeLlmClient(SemaphoreSlim gate) : ILlmClient
{
    private readonly FakeLlmClient _inner = new();

    /// <inheritdoc/>
    public async Task<LlmResponse> CompleteAsync(LlmRequest req, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { return await _inner.CompleteAsync(req, ct); }
        finally { gate.Release(); }
    }
}
