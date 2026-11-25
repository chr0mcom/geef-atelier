using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Research;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Research;

/// <summary>
/// <see cref="TavilySearchClient"/> over the real registration of the named client <c>tavily</c> (grounding and research
/// registrations, only the primary handler replaced): the body holds the topic and the fixed search settings and nothing else, every
/// failure is one request and an error code, and only raw text of http or https pages comes back, cut to the configured length.
/// </summary>
public sealed class TavilySearchClientTests
{
    private const string Topic = "Mindestlohn Deutschland 2026 Stundensatz";
    private const string Key = "tvly-test-key";

    private static (ITavilySearchClient Client, StubHttpHandler Handler, ServiceProvider Provider) Create(
        Func<HttpRequestMessage, string, CancellationToken, Task<HttpResponseMessage>> answer,
        string? key = Key, ResearchOptions? research = null)
    {
        var handler = new StubHttpHandler(answer);
        var values = new Dictionary<string, string?> { ["Tavily:Endpoint"] = "https://tavily.test.invalid" };
        if (key is not null)
            values["Tavily:ApiKey"] = key;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundingProviders(configuration);
        services.AddResearch(configuration);
        services.AddHttpClient(TavilySearchClient.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        if (research is not null)
            services.AddSingleton(Options.Create(research));
        var provider = services.BuildServiceProvider();
        return (provider.CreateScope().ServiceProvider.GetRequiredService<ITavilySearchClient>(), handler, provider);
    }

    private static string Results(params JsonObject[] results) =>
        new JsonObject
        {
            ["query"] = Topic,
            ["answer"] = "TAVILY-ANSWER-SENTINEL",
            ["results"] = new JsonArray(results.Select(r => (JsonNode)r).ToArray()),
            ["response_time"] = 0.4,
        }.ToJsonString();

    private static JsonObject Result(string url, string? raw, string title = "Page title") => new()
    {
        ["url"] = url,
        ["title"] = title,
        ["content"] = "TAVILY-SNIPPET-SENTINEL",
        ["raw_content"] = raw,
        ["score"] = 0.9,
    };

    private static Task<HttpResponseMessage> Ok(string json) => Task.FromResult(StubHttpHandler.Json(json));

    [Fact]
    public async Task The_body_holds_the_topic_and_the_fixed_settings_and_nothing_else()
    {
        var (client, handler, provider) = Create((_, _, _) => Ok(Results(Result("https://example.org/a", "Raw text of the page."))));
        await using var _ = provider;

        await client.SearchAsync(Topic, 3, CancellationToken.None);

        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://tavily.test.invalid/search", request.RequestUri!.ToString());
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal(
            ["api_key", "include_answer", "include_raw_content", "max_results", "query", "search_depth"],
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(Topic, root.GetProperty("query").GetString());
        Assert.Equal("basic", root.GetProperty("search_depth").GetString());
        Assert.Equal(3, root.GetProperty("max_results").GetInt32());
        Assert.True(root.GetProperty("include_raw_content").GetBoolean());
        Assert.False(root.GetProperty("include_answer").GetBoolean());
        Assert.Equal(Key, root.GetProperty("api_key").GetString());
    }

    [Fact]
    public async Task Only_url_title_and_raw_text_are_used()
    {
        var (client, _, provider) = Create((_, _, _) => Ok(Results(Result("https://example.org/a", "Raw text of the page.", "A title"))));
        await using var _ = provider;

        var outcome = await client.SearchAsync(Topic, 3, CancellationToken.None);

        Assert.Null(outcome.ErrorCode);
        var hit = Assert.Single(outcome.Hits);
        Assert.Equal(new TavilyHit("https://example.org/a", "A title", "Raw text of the page."), hit);
        Assert.DoesNotContain("SENTINEL", hit.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "http_500")]
    [InlineData(HttpStatusCode.BadGateway, "http_502")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "http_503")]
    [InlineData(HttpStatusCode.GatewayTimeout, "http_504")]
    [InlineData((HttpStatusCode)429, "http_429")]
    [InlineData(HttpStatusCode.Unauthorized, "http_401")]
    public async Task A_failed_answer_is_an_error_code_after_exactly_one_request(HttpStatusCode status, string code)
    {
        var (client, handler, provider) = Create((_, _, _) => Task.FromResult(StubHttpHandler.Json("""{"detail":"no"}""", status)));
        await using var _ = provider;

        var outcome = await client.SearchAsync(Topic, 3, CancellationToken.None);

        Assert.Equal(code, outcome.ErrorCode);
        Assert.Empty(outcome.Hits);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_slow_search_times_out_after_the_limit_with_one_request()
    {
        var (client, handler, provider) = Create(async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHttpHandler.Json(Results());
        }, research: new ResearchOptions { SearchTimeout = TimeSpan.FromMilliseconds(200) });
        await using var _ = provider;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var outcome = await client.SearchAsync(Topic, 3, CancellationToken.None);

        Assert.Equal(TavilySearchClient.TimeoutCode, outcome.ErrorCode);
        Assert.Single(handler.Requests);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
        Assert.InRange(outcome.DurationMs, 150, 10_000);
    }

    [Fact]
    public async Task A_broken_connection_is_an_error_code_after_one_request()
    {
        var (client, handler, provider) = Create((_, _, _) => throw new HttpRequestException("connection reset"));
        await using var _ = provider;

        var outcome = await client.SearchAsync(Topic, 3, CancellationToken.None);

        Assert.Equal(TavilySearchClient.HttpCode, outcome.ErrorCode);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"results":"nope"}""")]
    [InlineData("")]
    public async Task An_unreadable_answer_is_an_error_code(string body)
    {
        var (client, _, provider) = Create((_, _, _) => Ok(body));
        await using var _ = provider;

        var outcome = await client.SearchAsync(Topic, 3, CancellationToken.None);

        Assert.Equal(TavilySearchClient.InvalidAnswerCode, outcome.ErrorCode);
        Assert.Empty(outcome.Hits);
    }

    [Fact]
    public async Task Without_a_key_nothing_is_sent()
    {
        var (client, handler, provider) = Create((_, _, _) => Ok(Results()), key: null);
        await using var _ = provider;

        var outcome = await client.SearchAsync(Topic, 3, CancellationToken.None);

        Assert.Equal(TavilySearchClient.NoKeyCode, outcome.ErrorCode);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Results_without_raw_text_or_with_other_addresses_are_left_out_and_counted()
    {
        var (client, _, provider) = Create((_, _, _) => Ok(Results(
            Result("javascript:alert(1)", "Raw text."),
            Result("https://example.org/empty", null),
            Result("https://example.org/blank", "   "),
            Result("ftp://example.org/file", "Raw text."),
            Result("/relative/page", "Raw text."),
            Result("data:text/html,hi", "Raw text."),
            Result("http://example.org/kept", "Kept raw text."))));
        await using var _ = provider;

        var outcome = await client.SearchAsync(Topic, 10, CancellationToken.None);

        var hit = Assert.Single(outcome.Hits);
        Assert.Equal("http://example.org/kept", hit.Url);
        Assert.Equal(6, outcome.Skipped);
    }

    [Fact]
    public async Task Raw_text_is_cut_to_the_configured_length_without_splitting_a_character()
    {
        var raw = new string('a', 9) + "😀" + new string('b', 20);
        var (client, _, provider) = Create((_, _, _) => Ok(Results(Result("https://example.org/a", raw), Result("https://example.org/b", "short"))),
            research: new ResearchOptions { RawCharsPerPage = 10 });
        await using var _ = provider;

        var outcome = await client.SearchAsync(Topic, 3, CancellationToken.None);

        Assert.Equal(new string('a', 9), outcome.Hits[0].RawContent);
        Assert.Equal("short", outcome.Hits[1].RawContent);
    }

    [Fact]
    public async Task At_most_the_asked_number_of_results_comes_back()
    {
        var (client, _, provider) = Create((_, _, _) => Ok(Results(
            Result("https://example.org/1", "One."), Result("https://example.org/2", "Two."), Result("https://example.org/3", "Three."))));
        await using var _ = provider;

        var outcome = await client.SearchAsync(Topic, 2, CancellationToken.None);

        Assert.Equal(["https://example.org/1", "https://example.org/2"], outcome.Hits.Select(h => h.Url));
    }

    [Fact]
    public async Task Cancelling_the_caller_throws()
    {
        var (client, _, provider) = Create(async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return StubHttpHandler.Json(Results());
        });
        await using var _ = provider;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.SearchAsync(Topic, 3, cts.Token));
    }

    [Fact]
    public void The_named_client_has_no_retry_handler()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGroundingProviders(configuration);
        services.AddResearch(configuration);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<Microsoft.Extensions.Http.HttpClientFactoryOptions>>().Get(TavilySearchClient.HttpClientName);
        var builder = new RecordingHandlerBuilder(provider);
        foreach (var action in options.HttpMessageHandlerBuilderActions)
            action(builder);

        Assert.Empty(builder.AdditionalHandlers);
    }

    [Fact]
    public async Task The_end_to_end_fake_answers_scripted_searches_and_records_the_bodies()
    {
        var fake = new Geef.Atelier.Tests.EndToEnd.Fakes.FakeTavilyEndpoint(new Geef.Atelier.Tests.EndToEnd.Fakes.ExternalCallLog());
        fake.EnqueueSearch(new("https://example.org/a", "A", "Raw text A."), new("https://example.org/none", "B", null));
        fake.EnqueueStatus(HttpStatusCode.InternalServerError);
        var (client, _, provider) = Create((request, _, ct) => fake.HandleAsync(request, ct));
        await using var _ = provider;

        var first = await client.SearchAsync("first topic", 3, CancellationToken.None);
        var second = await client.SearchAsync("second topic", 3, CancellationToken.None);
        var third = await client.SearchAsync("third topic", 3, CancellationToken.None);

        Assert.Equal((1, 1), (first.Hits.Count, first.Skipped));
        Assert.Equal("Raw text A.", first.Hits[0].RawContent);
        Assert.Equal("http_500", second.ErrorCode);
        Assert.Null(third.ErrorCode);
        Assert.Empty(third.Hits);
        Assert.Equal(["first topic", "second topic", "third topic"],
            fake.RequestBodies.Select(b => JsonDocument.Parse(b).RootElement.GetProperty("query").GetString()));
    }

    private sealed class RecordingHandlerBuilder(IServiceProvider services) : Microsoft.Extensions.Http.HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; } = TavilySearchClient.HttpClientName;

        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services => services;

        public override HttpMessageHandler Build() => CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }
}
