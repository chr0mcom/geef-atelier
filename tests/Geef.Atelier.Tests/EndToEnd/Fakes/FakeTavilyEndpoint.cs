using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>One result of a scripted search: address, title and raw page text (<c>null</c> = no raw text).</summary>
internal sealed record FakeSearchHit(string Url, string Title, string? RawContent);

/// <summary>
/// Stand-in for <c>POST https://api.tavily.com/search</c>. Answers scripted searches (<see cref="EnqueueSearch"/>,
/// <see cref="EnqueueStatus"/>) in the order they were queued; without a script it returns one high-scoring result.
/// </summary>
internal sealed class FakeTavilyEndpoint(ExternalCallLog log)
{
    public const string ClientName = "tavily";

    private readonly ConcurrentQueue<Func<HttpResponseMessage>> _script = new();
    private readonly ConcurrentQueue<string> _bodies = new();

    /// <summary>Text placed into the answer and into the single result's content.</summary>
    public string ResultText { get; set; } = "E2E web result without a marker.";

    /// <summary>When set, every request is logged and then fails with this exception (a Tavily outage).</summary>
    public Exception? Failure { get; set; }

    /// <summary>Bodies of all requests in the order they arrived.</summary>
    public IReadOnlyList<string> RequestBodies => _bodies.ToList();

    /// <summary>The next unanswered search returns <paramref name="hits"/> with their raw text; the answer and snippets carry a marker that must never be used.</summary>
    public void EnqueueSearch(params FakeSearchHit[] hits) => _script.Enqueue(() => Answer(new JsonObject
    {
        ["query"] = "scripted",
        ["answer"] = "FAKE-TAVILY-ANSWER",
        ["results"] = new JsonArray(hits.Select(h => (JsonNode)new JsonObject
        {
            ["title"] = h.Title,
            ["url"] = h.Url,
            ["content"] = "FAKE-TAVILY-SNIPPET",
            ["raw_content"] = h.RawContent,
            ["score"] = 0.9,
        }).ToArray()),
    }));

    /// <summary>The next unanswered search fails with <paramref name="status"/>.</summary>
    public void EnqueueStatus(HttpStatusCode status) => _script.Enqueue(() => new HttpResponseMessage(status)
    {
        Content = new StringContent("""{"detail":"scripted failure"}""", Encoding.UTF8, "application/json"),
    });

    /// <summary>Handles one request sent through the named <c>tavily</c> client.</summary>
    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await ExternalCallLog.ReadBodyAsync(request, cancellationToken);
        _bodies.Enqueue(body);
        log.Add(new ExternalCall(ClientName, request.Method.Method, request.RequestUri?.ToString() ?? string.Empty, body));
        if (Failure is { } failure)
            throw failure;
        if (_script.TryDequeue(out var scripted))
            return scripted();

        return Answer(new JsonObject
        {
            ["query"] = "e2e",
            ["answer"] = ResultText,
            ["results"] = new JsonArray(new JsonObject
            {
                ["title"] = "E2E source",
                ["url"] = "https://example.org/e2e-source",
                ["content"] = ResultText,
                ["score"] = 0.95,
            }),
        });
    }

    private static HttpResponseMessage Answer(JsonObject payload) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
    };
}
