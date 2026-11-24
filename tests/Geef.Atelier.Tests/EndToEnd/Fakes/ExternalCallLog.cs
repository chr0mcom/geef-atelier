using System.Collections.Concurrent;

namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>One recorded request to an outside service other than the LLM endpoint.</summary>
internal sealed record ExternalCall(string Client, string Method, string Url, string Body);

/// <summary>Thread-safe log of requests to Tavily, embeddings, model listings and blocked clients.</summary>
internal sealed class ExternalCallLog
{
    private readonly ConcurrentQueue<ExternalCall> _calls = new();

    /// <summary>Stores a recorded request.</summary>
    public void Add(ExternalCall call) => _calls.Enqueue(call);

    /// <summary>All recorded requests of one named client.</summary>
    public IReadOnlyList<ExternalCall> For(string client) =>
        _calls.Where(c => c.Client == client).ToList();

    /// <summary>All recorded requests.</summary>
    public IReadOnlyList<ExternalCall> All => _calls.ToList();

    internal static async Task<string> ReadBodyAsync(HttpRequestMessage request, CancellationToken ct) =>
        request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
}
