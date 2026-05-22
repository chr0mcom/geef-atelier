using System.Net;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

// ── Shared test doubles for academic/rest-api grounding tests ───────────────

/// <summary>Named-client factory that always returns the same <see cref="HttpClient"/>.</summary>
internal sealed class SingleNamedHttpClientFactory(string expectedName, HttpClient client)
    : IHttpClientFactory
{
    public HttpClient CreateClient(string name)
    {
        if (!string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unexpected client name '{name}'; expected '{expectedName}'.");
        return client;
    }
}

/// <summary>Named-client factory that returns a fixed client regardless of name.</summary>
internal sealed class AnyNameHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

/// <summary>Returns a fixed sequence of <see cref="HttpResponseMessage"/> per call.</summary>
internal sealed class SequencedHttpHandler(IEnumerable<(HttpStatusCode StatusCode, string? Body)> responses)
    : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode, string?)> _queue = new(responses);
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        if (_queue.Count == 0)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var (code, body) = _queue.Dequeue();
        var resp = new HttpResponseMessage(code);
        if (body is not null)
            resp.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        return Task.FromResult(resp);
    }
}

/// <summary>Fake academic source that returns a fixed list of papers.</summary>
internal sealed class FakeAcademicSource(string sourceName, IReadOnlyList<AcademicPaper> papers) : IAcademicSource
{
    public string SourceName => sourceName;
    public string? LastQuery { get; private set; }

    public Task<IReadOnlyList<AcademicPaper>> SearchAsync(string query, AcademicSearchOptions options, CancellationToken ct)
    {
        LastQuery = query;
        return Task.FromResult(papers);
    }
}

/// <summary>In-memory repository for grounding consultations.</summary>
internal sealed class InMemoryConsultationRepo : IGroundingConsultationRepository
{
    private readonly List<GroundingConsultation> _store = [];

    public Task<GroundingConsultation> CreateAsync(GroundingConsultation consultation, CancellationToken ct)
    {
        _store.Add(consultation);
        return Task.FromResult(consultation);
    }

    public Task<IReadOnlyList<GroundingConsultation>> GetByRunIdAsync(Guid runId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<GroundingConsultation>>(
            _store.Where(c => c.RunId == runId).ToList());

    public Task UpdateRefinementOutcomeAsync(Guid consultationId, RefinementOutcome outcome, CancellationToken ct)
        => Task.CompletedTask;
}

/// <summary>SSRF validator that always allows any URL.</summary>
internal sealed class AlwaysAllowSafetyValidator : Geef.Atelier.Infrastructure.Security.IUrlSafetyValidator
{
    public Task<Geef.Atelier.Infrastructure.Security.UrlSafetyResult> ValidateAsync(Uri uri, CancellationToken ct)
        => Task.FromResult(new Geef.Atelier.Infrastructure.Security.UrlSafetyResult(IsAllowed: true, RejectionReason: null));
}

/// <summary>SSRF validator that always blocks any URL.</summary>
internal sealed class AlwaysBlockSafetyValidator : Geef.Atelier.Infrastructure.Security.IUrlSafetyValidator
{
    public Task<Geef.Atelier.Infrastructure.Security.UrlSafetyResult> ValidateAsync(Uri uri, CancellationToken ct)
        => Task.FromResult(new Geef.Atelier.Infrastructure.Security.UrlSafetyResult(IsAllowed: false, RejectionReason: "blocked-by-test"));
}

/// <summary>Helper to build a <see cref="GroundingProviderProfile"/> for REST-API tests.</summary>
internal static class RestApiProfileBuilder
{
    public static GroundingProviderProfile Make(
        string url,
        string method = "GET",
        string? responsePath = null,
        int maxItems = 10,
        string? authHeaderEnv = null,
        string? bodyTemplate = null,
        Dictionary<string, string>? extraSettings = null)
    {
        var settings = new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyRestApiUrl] = url,
            [GroundingProviderProfile.KeyRestApiMethod] = method,
            [GroundingProviderProfile.KeyRestApiMaxItems] = maxItems.ToString(),
        };
        if (responsePath is not null)
            settings[GroundingProviderProfile.KeyRestApiResponsePath] = responsePath;
        if (authHeaderEnv is not null)
            settings[GroundingProviderProfile.KeyRestApiAuthHeaderEnv] = authHeaderEnv;
        if (bodyTemplate is not null)
            settings[GroundingProviderProfile.KeyRestApiBodyTemplate] = bodyTemplate;
        if (extraSettings is not null)
            foreach (var (k, v) in extraSettings)
                settings[k] = v;

        return new GroundingProviderProfile(
            Name:             "test-rest",
            DisplayName:      "Test REST",
            Description:      "Test",
            ProviderType:     GroundingProviderTypes.RestApi,
            ProviderSettings: settings,
            MaxQueriesPerRun: null,
            IsSystem:         false);
    }
}

/// <summary>Helper to build a <see cref="GroundingProviderProfile"/> for academic-search tests.</summary>
internal static class AcademicProfileBuilder
{
    public static GroundingProviderProfile Make(
        string source = "semantic-scholar",
        int maxPapers = 5,
        string? apiKeyEnv = null)
    {
        var settings = new Dictionary<string, string>
        {
            [GroundingProviderProfile.KeyAcademicSource]    = source,
            [GroundingProviderProfile.KeyAcademicMaxPapers] = maxPapers.ToString(),
        };
        if (apiKeyEnv is not null)
            settings[GroundingProviderProfile.KeyAcademicApiKeyEnv] = apiKeyEnv;

        return new GroundingProviderProfile(
            Name:             "test-academic",
            DisplayName:      "Test Academic",
            Description:      "Test",
            ProviderType:     GroundingProviderTypes.AcademicSearch,
            ProviderSettings: settings,
            MaxQueriesPerRun: null,
            IsSystem:         false);
    }
}
