using System.Net;
using System.Xml.Linq;
using Geef.Atelier.Application.Crew.Grounding;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding.AcademicSearch;

/// <summary>
/// arXiv Atom/XML adapter. Free, no API key. Returns preprints from CS/Physics/Math/etc.
/// Rate-limit: moderately strict — uses a 1 s polite delay between calls.
/// </summary>
internal sealed class ArxivSource(IHttpClientFactory httpClientFactory, ILogger<ArxivSource> logger) : IAcademicSource
{
    private static readonly XNamespace AtomNs = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace ArxivNs = "http://arxiv.org/schemas/atom";

    public string SourceName => "arxiv";

    public async Task<IReadOnlyList<AcademicPaper>> SearchAsync(string query, AcademicSearchOptions options, CancellationToken ct)
    {
        var encodedQuery = Uri.EscapeDataString(BuildQuery(query, options));
        var url = $"https://export.arxiv.org/api/query?search_query={encodedQuery}&start=0&max_results={options.MaxPapers}&sortBy=relevance&sortOrder=descending";

        var http = httpClientFactory.CreateClient("academic-arxiv");
        string xml;
        try
        {
            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            xml = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "ArxivSource: HTTP request failed for query '{Query}'.", query);
            return [];
        }

        return ParseAtomXml(xml, options.MaxPapers);
    }

    private static string BuildQuery(string query, AcademicSearchOptions options)
    {
        var q = string.IsNullOrWhiteSpace(options.Fields)
            ? $"all:{query}"
            : $"{options.Fields}:{query}";

        if (!string.IsNullOrWhiteSpace(options.DateFrom))
            q += $" AND submittedDate:[{NormalizeDate(options.DateFrom)} TO *]";

        return q;
    }

    private static string NormalizeDate(string dateFrom)
    {
        // arXiv uses YYYYMMDDHHMMSS format; accept YYYY or YYYY-MM-DD
        if (dateFrom.Length == 4 && int.TryParse(dateFrom, out _))
            return $"{dateFrom}0101000000";
        if (dateFrom.Length >= 10)
            return dateFrom[..10].Replace("-", "") + "000000";
        return dateFrom;
    }

    private IReadOnlyList<AcademicPaper> ParseAtomXml(string xml, int maxPapers)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ArxivSource: failed to parse Atom XML.");
            return [];
        }

        var papers = new List<AcademicPaper>();
        foreach (var entry in doc.Descendants(AtomNs + "entry").Take(maxPapers))
        {
            var title = entry.Element(AtomNs + "title")?.Value?.Trim()
                            ?.Replace("\n", " ")
                            .Replace("  ", " ");
            if (string.IsNullOrWhiteSpace(title))
                continue;

            var idRaw = entry.Element(AtomNs + "id")?.Value?.Trim();
            var arxivId = ExtractArxivId(idRaw);
            var url = idRaw;

            var authors = entry.Elements(AtomNs + "author")
                .Select(a => a.Element(AtomNs + "name")?.Value?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();
            var authorsStr = authors.Count == 0 ? null : string.Join("; ", authors);

            var summary = entry.Element(AtomNs + "summary")?.Value?.Trim()
                             ?.Replace("\n", " ")
                             .Replace("  ", " ");

            // DOI link: <link title="doi" href="https://doi.org/10.xxx">
            var doiLink = entry.Elements(AtomNs + "link")
                .FirstOrDefault(l => string.Equals(l.Attribute("title")?.Value, "doi", StringComparison.OrdinalIgnoreCase));
            var doi = ExtractDoi(doiLink?.Attribute("href")?.Value);

            DateTimeOffset? published = null;
            var publishedStr = entry.Element(AtomNs + "published")?.Value;
            if (!string.IsNullOrWhiteSpace(publishedStr) &&
                DateTimeOffset.TryParse(publishedStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                published = dt;

            papers.Add(new AcademicPaper(
                Title: title,
                Authors: authorsStr,
                Abstract: summary,
                Doi: doi,
                ArxivId: arxivId,
                Url: url,
                PublishedDate: published));
        }

        return papers;
    }

    private static string? ExtractArxivId(string? idUrl)
    {
        if (string.IsNullOrWhiteSpace(idUrl))
            return null;
        // Format: http://arxiv.org/abs/2301.07xxx
        var idx = idUrl.LastIndexOf('/');
        return idx >= 0 ? idUrl[(idx + 1)..] : null;
    }

    private static string? ExtractDoi(string? doiUrl)
    {
        if (string.IsNullOrWhiteSpace(doiUrl))
            return null;
        const string prefix = "https://doi.org/";
        return doiUrl.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? doiUrl[prefix.Length..] : doiUrl;
    }
}
