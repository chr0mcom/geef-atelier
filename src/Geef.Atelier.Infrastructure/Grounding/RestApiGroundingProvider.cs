using System.Net;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Grounds a briefing by calling an arbitrary HTTP REST endpoint.
/// Security-critical path: template substitution → SSRF validation → fetch → JSONPath extraction.
/// Auth tokens are read from environment variables at runtime — never stored as plaintext.
/// </summary>
internal sealed class RestApiGroundingProvider(
    IHttpClientFactory httpClientFactory,
    IUrlSafetyValidator urlSafetyValidator,
    IServiceScopeFactory scopeFactory,
    ILogger<RestApiGroundingProvider> logger) : IGroundingProvider
{
    public string ProviderType => GroundingProviderTypes.RestApi;

    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        var templateUrl = profile.RestApiUrl;
        if (string.IsNullOrWhiteSpace(templateUrl))
            throw new InvalidOperationException(
                $"REST-API grounding provider '{profile.Name}' has no 'url' configured.");

        // 1. Template substitution — BEFORE SSRF validation (injection guard: a crafted briefing must not sneak in an internal URL)
        var finalUrl = templateUrl.Replace("{briefing}", Uri.EscapeDataString(briefingText));

        // 2. SSRF validation on the final (substituted) URL
        if (!Uri.TryCreate(finalUrl, UriKind.Absolute, out var parsedUri))
            throw new InvalidOperationException(
                $"REST-API grounding provider '{profile.Name}': URL '{finalUrl}' is not a valid absolute URI.");

        var safetyCheck = await urlSafetyValidator.ValidateAsync(parsedUri, ct);
        if (!safetyCheck.IsAllowed)
            throw new InvalidOperationException(
                $"REST-API grounding provider '{profile.Name}': URL blocked by SSRF guard — {safetyCheck.RejectionReason}");

        // 3. Secret-auth: resolve token from env var at runtime — never log the value
        var authToken = ResolveAuthToken(profile);
        var authConfigured = authToken is not null;

        logger.LogInformation(
            "RestApi grounding: run={RunId} provider={Profile} url={Host} method={Method} auth={AuthConfigured}",
            runId, profile.Name, parsedUri.Host + parsedUri.AbsolutePath, profile.RestApiMethod,
            authConfigured ? "configured" : "none");

        string responseBody;
        try
        {
            responseBody = await ExecuteRequestAsync(profile, parsedUri, briefingText, authToken, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "RestApi grounding: run={RunId} provider={Profile} HTTP request failed.", runId, profile.Name);
            throw;
        }

        // 4. JSONPath extraction
        var items = ExtractItems(responseBody, profile.RestApiResponsePath, profile.RestApiMaxItems, profile.Name);

        var citation = BuildCitation(profile, parsedUri);
        var enrichedContext = BuildEnrichedContext(profile, parsedUri, items);
        var consultationId = await PersistConsultationAsync(runId, profile.Name, finalUrl, [citation], 0, null, ct);

        return new GroundingResult(
            ProviderName: profile.Name,
            EnrichedContext: enrichedContext,
            Citations: [citation],
            TokensOrCreditsUsed: 0,
            CostEur: 0m,
            ConsultationId: consultationId);
    }

    private async Task<string> ExecuteRequestAsync(
        GroundingProviderProfile profile,
        Uri uri,
        string briefingText,
        string? authToken,
        CancellationToken ct)
    {
        var http = httpClientFactory.CreateClient("rest-api-grounding");
        using var request = new HttpRequestMessage(
            profile.RestApiMethod == "POST" ? HttpMethod.Post : HttpMethod.Get,
            uri);

        // Auth header — never log the token value
        if (authToken is not null)
        {
            var headerValue = profile.RestApiAuthHeaderFormat.Replace("{token}", authToken);
            request.Headers.TryAddWithoutValidation(profile.RestApiAuthHeaderName, headerValue);
        }

        // Additional headers from settings
        foreach (var (header, value) in profile.RestApiHeaders)
            request.Headers.TryAddWithoutValidation(header, value);

        // POST body
        if (profile.RestApiMethod == "POST" && profile.RestApiBodyTemplate is { Length: > 0 } bodyTemplate)
        {
            var body = bodyTemplate.Replace("{briefing}", JsonEscape(briefingText));
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }

    private static string? ResolveAuthToken(GroundingProviderProfile profile)
    {
        if (profile.RestApiAuthHeaderEnv is not { Length: > 0 } envVar)
            return null;
        var token = Environment.GetEnvironmentVariable(envVar);
        // Intentionally not logging the token — only log presence/absence
        return string.IsNullOrWhiteSpace(token) ? null : token;
    }

    private List<string> ExtractItems(string json, string? responsePath, int maxItems, string providerName)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];

        JsonElement root;
        try
        {
            root = JsonDocument.Parse(json).RootElement;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "RestApi grounding: provider={Provider} failed to parse JSON response.", providerName);
            return [json.Length > 2000 ? json[..2000] : json];
        }

        IReadOnlyList<JsonElement> selected;
        if (!string.IsNullOrWhiteSpace(responsePath))
        {
            selected = JsonPathNavigator.Select(root, responsePath);
        }
        else
        {
            selected = [root];
        }

        var items = new List<string>();
        foreach (var element in selected.Take(maxItems))
        {
            var text = ElementToText(element);
            if (!string.IsNullOrWhiteSpace(text))
                items.Add(text);
        }

        return items;
    }

    private static string ElementToText(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.String  => element.GetString() ?? string.Empty,
            JsonValueKind.Number  => element.GetRawText(),
            JsonValueKind.True    => "true",
            JsonValueKind.False   => "false",
            JsonValueKind.Object  => ObjectToText(element),
            JsonValueKind.Array   => string.Join("\n", element.EnumerateArray().Select(ElementToText)),
            _                     => string.Empty,
        };

    private static string ObjectToText(JsonElement obj)
    {
        var sb = new StringBuilder();
        foreach (var prop in obj.EnumerateObject())
        {
            var value = ElementToText(prop.Value);
            if (!string.IsNullOrWhiteSpace(value))
                sb.AppendLine($"{prop.Name}: {value}");
        }
        return sb.ToString().TrimEnd();
    }

    private static SourceCitation BuildCitation(GroundingProviderProfile profile, Uri uri)
    {
        var label = profile.ProviderSettings.TryGetValue("label", out var l) && !string.IsNullOrWhiteSpace(l)
            ? l
            : uri.Host;
        var apiRef = $"api://{uri.Host}{uri.AbsolutePath}";

        return new SourceCitation(
            Title: label,
            Url: apiRef,
            Snippet: $"Data from {uri.Host}",
            DocumentReference: null,
            RelevanceScore: null,
            PublishedDate: null);
    }

    private static string BuildEnrichedContext(GroundingProviderProfile profile, Uri uri, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine($"[REST-API context — source: {uri.Host}]");
        sb.AppendLine("Use the data below only where clearly relevant to the briefing.");
        sb.AppendLine();

        for (var i = 0; i < items.Count; i++)
        {
            sb.AppendLine($"--- Item {i + 1} ---");
            sb.AppendLine(items[i]);
            sb.AppendLine();
        }

        sb.Append("[End of REST-API context]");
        return sb.ToString();
    }

    private async Task<Guid> PersistConsultationAsync(
        Guid runId,
        string providerName,
        string query,
        IReadOnlyList<SourceCitation> citations,
        int tokensOrCredits,
        decimal? costEur,
        CancellationToken ct)
    {
        var consultation = new GroundingConsultation(
            Id:                    Guid.NewGuid(),
            RunId:                 runId,
            GroundingProviderName: providerName,
            Query:                 query,
            Citations:             citations,
            TokensOrCreditsUsed:   tokensOrCredits,
            CostEur:               costEur,
            CreatedAt:             DateTimeOffset.UtcNow);

        await using var scope = scopeFactory.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();
        await repo.CreateAsync(consultation, ct);
        return consultation.Id;
    }

    private static string JsonEscape(string value) =>
        System.Text.Json.JsonSerializer.Serialize(value)[1..^1]; // strip surrounding quotes
}
