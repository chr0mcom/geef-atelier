using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Geef.Atelier.Tests.EndToEnd.Fakes;

/// <summary>
/// Stand-in for OpenRouter's <c>POST /embeddings</c>. Produces deterministic bag-of-words vectors
/// (hashed into the configured dimension and L2-normalised), so similar texts get similar vectors and
/// the real pgvector search returns meaningful neighbours.
/// </summary>
internal sealed class FakeEmbeddingsEndpoint(ExternalCallLog log, int dimensions)
{
    public const string ClientName = "embeddings";

    /// <summary>Handles one request sent through the named <c>embeddings</c> client.</summary>
    public async Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = await ExternalCallLog.ReadBodyAsync(request, cancellationToken);
        log.Add(new ExternalCall(ClientName, request.Method.Method, request.RequestUri?.ToString() ?? string.Empty, body));

        var inputs = ReadInputs(body);
        var data = new JsonArray();
        var totalTokens = 0;
        for (var i = 0; i < inputs.Count; i++)
        {
            var vector = Embed(inputs[i], dimensions);
            data.Add(new JsonObject
            {
                ["object"] = "embedding",
                ["index"] = i,
                ["embedding"] = new JsonArray(vector.Select(v => (JsonNode)v).ToArray()),
            });
            totalTokens += Math.Max(1, inputs[i].Length / 4);
        }

        var payload = new JsonObject
        {
            ["object"] = "list",
            ["data"] = data,
            ["usage"] = new JsonObject { ["prompt_tokens"] = totalTokens, ["total_tokens"] = totalTokens },
        };

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    internal static float[] Embed(string text, int dimensions)
    {
        var vector = new float[dimensions];
        foreach (var token in Tokenize(text))
            vector[(int)(Fnv1A(token) % (uint)dimensions)] += 1f;

        var norm = Math.Sqrt(vector.Sum(v => (double)v * v));
        if (norm == 0)
        {
            vector[0] = 1f;
            return vector;
        }

        for (var i = 0; i < vector.Length; i++)
            vector[i] = (float)(vector[i] / norm);
        return vector;
    }

    private static IReadOnlyList<string> ReadInputs(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var input = doc.RootElement.GetProperty("input");
        return input.ValueKind == JsonValueKind.Array
            ? input.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList()
            : [input.GetString() ?? string.Empty];
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
                continue;
            }

            if (sb.Length > 0)
            {
                yield return sb.ToString();
                sb.Clear();
            }
        }

        if (sb.Length > 0)
            yield return sb.ToString();
    }

    private static uint Fnv1A(string token)
    {
        var hash = 2166136261u;
        foreach (var ch in token)
        {
            hash ^= ch;
            hash *= 16777619u;
        }
        return hash;
    }
}
