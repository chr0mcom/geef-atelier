using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>Stand-in for <c>POST https://api.tavily.com/search</c>; returns one high-scoring result.</summary>
internal sealed class FakeTavilyEndpoint(ExternalCallLog log)
{
    public const string ClientName = "tavily";

    /// <summary>Text placed into the answer and into the single result's content.</summary>
    public string ResultText { get; set; } = "E2E web result without a marker.";

    /// <summary>When set, every request is logged and then fails with this exception (a Tavily outage).</summary>
    public Exception? Failure { get; set; }

    /// <summary>Handles one request sent through the named <c>tavily</c> client.</summary>
    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await ExternalCallLog.ReadBodyAsync(request, cancellationToken);
        log.Add(new ExternalCall(ClientName, request.Method.Method, request.RequestUri?.ToString() ?? string.Empty, body));
        if (Failure is { } failure)
            throw failure;

        var payload = new JsonObject
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
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }
}
