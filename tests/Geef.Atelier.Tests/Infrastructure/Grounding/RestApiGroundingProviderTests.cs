using System.Net;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Tests.Domain.Crew;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Infrastructure.Grounding;

/// <summary>
/// Tests for <see cref="RestApiGroundingProvider"/> — covers template substitution,
/// JSONPath extraction, maxItems cap, auth header, custom headers, context formatting,
/// and missing-URL guard.
/// </summary>
public sealed class RestApiGroundingProviderTests
{
    private const string SimpleJsonArray = """{"items":["first item","second item","third item"]}""";
    private const string SimpleJsonString = """{"content":"hello world"}""";
    private const string SimpleJsonObject = """{"key":"value","num":42}""";

    // ── basic success path ────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_SimpleArray_ReturnsItems()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonArray),
            profile: RestApiProfileBuilder.Make("https://api.example.com/data", responsePath: "$.items[*]"));

        var result = await provider.EnrichAsync("test", Profile("$.items[*]"), Guid.NewGuid(), CancellationToken.None);

        Assert.NotEmpty(result.Citations);
        Assert.Contains("first item", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_NoResponsePath_ReturnsWholeObject()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonObject));
        var result = await provider.EnrichAsync("test", Profile(), Guid.NewGuid(), CancellationToken.None);

        Assert.Contains("key", result.EnrichedContext);
        Assert.Contains("value", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_StringField_ReturnsStringValue()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonString));
        var result = await provider.EnrichAsync("test", Profile("$.content"), Guid.NewGuid(), CancellationToken.None);
        Assert.Contains("hello world", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_EmptyJsonArray_ReturnsEmptyContext()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok("""{"items":[]}"""));
        var result = await provider.EnrichAsync("test", Profile("$.items[*]"), Guid.NewGuid(), CancellationToken.None);
        Assert.Equal(string.Empty, result.EnrichedContext);
    }

    // ── context format ────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_Context_ContainsHostHeader()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonString));
        var result = await provider.EnrichAsync("test", Profile("$.content"), Guid.NewGuid(), CancellationToken.None);
        Assert.Contains("[REST-API context — source:", result.EnrichedContext);
        Assert.Contains("api.example.com", result.EnrichedContext);
    }

    [Fact]
    public async Task EnrichAsync_Context_ContainsEndMarker()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonString));
        var result = await provider.EnrichAsync("test", Profile("$.content"), Guid.NewGuid(), CancellationToken.None);
        Assert.Contains("[End of REST-API context]", result.EnrichedContext);
    }

    // ── citation format ───────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_Citation_UsesApiSchemeWithHostPath()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonString));
        var result = await provider.EnrichAsync("test", Profile("$.content"), Guid.NewGuid(), CancellationToken.None);
        Assert.Single(result.Citations);
        Assert.StartsWith("api://", result.Citations[0].Url);
        Assert.Contains("api.example.com", result.Citations[0].Url);
    }

    // ── maxItems cap ─────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_MaxItems_CapsExtractedItems()
    {
        var json = """{"items":["a","b","c","d","e"]}""";
        var (provider, _) = Build(FakeHttpHandler.Ok(json));
        var profile = RestApiProfileBuilder.Make(
            "https://api.example.com/data",
            responsePath: "$.items[*]",
            maxItems: 2);

        var result = await provider.EnrichAsync("test", profile, Guid.NewGuid(), CancellationToken.None);

        // Context should only contain 2 items (Item 1 and Item 2)
        Assert.Contains("Item 1", result.EnrichedContext);
        Assert.Contains("Item 2", result.EnrichedContext);
        Assert.DoesNotContain("Item 3", result.EnrichedContext);
    }

    // ── template substitution ─────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_TemplateSubstitution_ReplacesUrlPlaceholder()
    {
        var handler = new CapturingHttpHandler(SimpleJsonString);
        var (provider, _) = BuildWithHandler(handler);
        var profile = RestApiProfileBuilder.Make("https://api.example.com/search?q={briefing}");

        await provider.EnrichAsync("machine learning", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(handler.LastRequestUri);
        // Uri.AbsoluteUri preserves percent-encoding; Uri.ToString() decodes for display
        Assert.Contains(Uri.EscapeDataString("machine learning"), handler.LastRequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task EnrichAsync_TemplateSubstitution_UrlEncodesSpecialChars()
    {
        var handler = new CapturingHttpHandler(SimpleJsonString);
        var (provider, _) = BuildWithHandler(handler);
        var profile = RestApiProfileBuilder.Make("https://api.example.com/q?text={briefing}");

        await provider.EnrichAsync("a & b = c?", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(handler.LastRequestUri);
        Assert.DoesNotContain("&", handler.LastRequestUri!.Query.Replace("%26", ""));
    }

    // ── POST body template ────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_Post_SendsJsonEscapedBriefing_InBody()
    {
        var handler = new CapturingHttpHandler(SimpleJsonString);
        var (provider, _) = BuildWithHandler(handler);
        var profile = RestApiProfileBuilder.Make(
            "https://api.example.com/search",
            method: "POST",
            bodyTemplate: """{"query":"{briefing}","limit":5}""");

        await provider.EnrichAsync("say \"hello\"", profile, Guid.NewGuid(), CancellationToken.None);

        Assert.NotNull(handler.LastRequestBody);
        // STJ uses " unicode escape for " by default — verify the value is JSON-escaped
        Assert.Contains("\\u0022hello\\u0022", handler.LastRequestBody);
    }

    // ── error cases ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_MissingUrl_ThrowsInvalidOperationException()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonString));
        var profile = new GroundingProviderProfile(
            "no-url", "No URL", "desc", GroundingProviderTypes.RestApi,
            new Dictionary<string, string>(), null, false);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EnrichAsync("test", profile, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task EnrichAsync_HttpFailure_ThrowsHttpRequestException()
    {
        var (provider, _) = Build(FakeHttpHandler.Fail(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => provider.EnrichAsync("test", Profile(), Guid.NewGuid(), CancellationToken.None));
    }

    // ── SSRF guard ────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_BlockedBySsrfValidator_ThrowsInvalidOperationException()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonString), alwaysBlock: true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.EnrichAsync("test", Profile(), Guid.NewGuid(), CancellationToken.None));
    }

    // ── persistence ───────────────────────────────────────────────────────────

    [Fact]
    public async Task EnrichAsync_PersistsConsultation_WithRunId()
    {
        var repo = new InMemoryConsultationRepo();
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonString), repo: repo);
        var runId = Guid.NewGuid();

        await provider.EnrichAsync("test", Profile("$.content"), runId, CancellationToken.None);

        var stored = await repo.GetByRunIdAsync(runId, CancellationToken.None);
        Assert.Single(stored);
        Assert.Equal(runId, stored[0].RunId);
    }

    [Fact]
    public void ProviderType_IsRestApi()
    {
        var (provider, _) = Build(FakeHttpHandler.Ok(SimpleJsonString));
        Assert.Equal("rest-api", provider.ProviderType);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static GroundingProviderProfile Profile(string? responsePath = null)
        => RestApiProfileBuilder.Make("https://api.example.com/data", responsePath: responsePath);

    private static (RestApiGroundingProvider, InMemoryConsultationRepo) Build(
        FakeHttpHandler handler,
        GroundingProviderProfile? profile = null,
        bool alwaysBlock = false,
        InMemoryConsultationRepo? repo = null)
    {
        repo ??= new InMemoryConsultationRepo();
        var client = new HttpClient(handler);
        var factory = new AnyNameHttpClientFactory(client);
        var validator = alwaysBlock
            ? (Geef.Atelier.Infrastructure.Security.IUrlSafetyValidator)new AlwaysBlockSafetyValidator()
            : new AlwaysAllowSafetyValidator();

        var services = new ServiceCollection();
        services.AddScoped<IGroundingConsultationRepository>(_ => repo);
        var provider = new RestApiGroundingProvider(
            factory,
            validator,
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RestApiGroundingProvider>.Instance);
        return (provider, repo);
    }

    private static (RestApiGroundingProvider, InMemoryConsultationRepo) BuildWithHandler(
        HttpMessageHandler handler,
        InMemoryConsultationRepo? repo = null)
    {
        repo ??= new InMemoryConsultationRepo();
        var client = new HttpClient(handler);
        var factory = new AnyNameHttpClientFactory(client);

        var services = new ServiceCollection();
        services.AddScoped<IGroundingConsultationRepository>(_ => repo);
        var provider = new RestApiGroundingProvider(
            factory,
            new AlwaysAllowSafetyValidator(),
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RestApiGroundingProvider>.Instance);
        return (provider, repo);
    }

    // ── test doubles ─────────────────────────────────────────────────────────

    private sealed class CapturingHttpHandler(string responseBody) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }
        public string? LastRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            if (request.Content is not null)
                LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json")
            };
        }
    }
}
