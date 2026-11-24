using System.Net;
using System.Text;
using Geef.Atelier.Core.Domain.Crew.Models;

namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>Stand-in for the cli-proxy model listing <c>GET /v1/cli/{provider}/models</c>.</summary>
internal sealed class FakeCliProxyModelsEndpoint(ExternalCallLog log)
{
    public const string ClientName = "cli-proxy-models";

    /// <summary>Handles one request sent through the named <c>cli-proxy-models</c> client.</summary>
    public Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.ToString() ?? string.Empty;
        log.Add(new ExternalCall(ClientName, request.Method.Method, url, string.Empty));

        var provider = url.Contains($"/{ModelMatrix.CodexCli}/", StringComparison.Ordinal)
            ? ModelMatrix.CodexCli
            : ModelMatrix.ClaudeCli;
        var ids = ModelMatrix.All.Where(c => c.Provider == provider).Select(c => c.Model);
        var payload = ScriptedLlmEndpoint.ModelList(ids);

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        });
    }
}
