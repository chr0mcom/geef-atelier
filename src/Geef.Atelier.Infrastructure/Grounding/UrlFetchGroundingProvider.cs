using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

internal sealed class UrlFetchGroundingProvider(
    IHttpClientFactory httpClientFactory,
    IUrlSafetyValidator urlSafetyValidator,
    IHtmlContentExtractor htmlExtractor,
    IServiceScopeFactory scopeFactory,
    ILogger<UrlFetchGroundingProvider> logger) : IGroundingProvider
{
    public string ProviderType => GroundingProviderTypes.UrlFetch;

    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        var urls = profile.Urls;
        var citations = new List<SourceCitation>();
        var contextParts = new List<(SourceCitation Citation, string CleanedText)>();

        var httpClient = httpClientFactory.CreateClient("url-fetch");

        foreach (var urlString in urls)
        {
            if (!Uri.TryCreate(urlString, UriKind.Absolute, out var uri))
            {
                logger.LogWarning("UrlFetch grounding: run={RunId} could not parse URL '{Url}' — skipping.", runId, urlString);
                continue;
            }

            var safetyCheck = await urlSafetyValidator.ValidateAsync(uri, ct);
            if (!safetyCheck.IsAllowed)
            {
                logger.LogWarning(
                    "UrlFetch grounding: run={RunId} URL '{Url}' blocked: {Reason} — skipping.",
                    runId, uri, safetyCheck.RejectionReason);
                continue;
            }

            string html;
            try
            {
                var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                int redirectCount = 0;
                while ((int)response.StatusCode is >= 301 and <= 308 && redirectCount < 3)
                {
                    var location = response.Headers.Location;
                    if (location is null) break;
                    var redirectUri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                    var redirectSafetyCheck = await urlSafetyValidator.ValidateAsync(redirectUri, ct);
                    if (!redirectSafetyCheck.IsAllowed)
                    {
                        logger.LogWarning(
                            "UrlFetch grounding: run={RunId} redirect to '{RedirectUrl}' blocked: {Reason} — skipping original URL.",
                            runId, redirectUri, redirectSafetyCheck.RejectionReason);
                        response = null!;
                        break;
                    }
                    uri = redirectUri;
                    response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                    redirectCount++;
                }

                if (response is null)
                    continue;

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "UrlFetch grounding: run={RunId} URL '{Url}' returned HTTP {StatusCode} — skipping.",
                        runId, uri, (int)response.StatusCode);
                    continue;
                }

                html = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
            {
                logger.LogWarning(ex, "UrlFetch grounding: run={RunId} failed to fetch '{Url}' — skipping.", runId, uri);
                continue;
            }

            HtmlExtractionResult extraction;
            try
            {
                extraction = await htmlExtractor.ExtractAsync(html, profile.StripBoilerplate, ct);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "UrlFetch grounding: run={RunId} HTML extraction failed for '{Url}' — skipping.", runId, uri);
                continue;
            }

            var cleanedText = extraction.Text;
            var maxLength = profile.MaxContentPerUrl;
            if (cleanedText.Length > maxLength)
                cleanedText = cleanedText[..maxLength];

            var snippet = cleanedText.Length <= 300 ? cleanedText : cleanedText[..300];

            var citation = new SourceCitation(
                Title: extraction.Title ?? uri.ToString(),
                Url: uri.AbsoluteUri,
                Snippet: snippet,
                DocumentReference: null,
                RelevanceScore: null,
                PublishedDate: null);

            citations.Add(citation);
            contextParts.Add((citation, cleanedText));
        }

        var enrichedContext = BuildEnrichedContext(contextParts);
        var consultationId = await PersistConsultationAsync(runId, profile.Name, briefingText, citations, 0, null, ct);

        return new GroundingResult(
            ProviderName: profile.Name,
            EnrichedContext: enrichedContext,
            Citations: citations,
            TokensOrCreditsUsed: 0,
            CostEur: 0m,
            ConsultationId: consultationId);
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
        var consultationRepository = scope.ServiceProvider.GetRequiredService<IGroundingConsultationRepository>();
        await consultationRepository.CreateAsync(consultation, ct);
        return consultation.Id;
    }

    private static string BuildEnrichedContext(IReadOnlyList<(SourceCitation Citation, string CleanedText)> parts)
    {
        if (parts.Count == 0)
            return string.Empty;

        var sb = new System.Text.StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            var (citation, text) = parts[i];
            sb.Append($"## Quelle {i + 1}: {citation.Title}\nURL: {citation.Url}\n\n{text}\n\n");
        }
        return sb.ToString().TrimEnd();
    }
}
