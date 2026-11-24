using System.Net;
using System.Text;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Tests.Pipeline;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Llm;

/// <summary>D-066: a cli-proxy failover is the only trace that a reviewer ran on another CLI's default model.</summary>
public sealed class OpenAiCompatibleClientFailoverTests
{
    private const string Ok = """{"choices":[{"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":1,"total_tokens":2}}""";

    [Fact]
    public async Task LogsAWarningWhenTheProxyFailedOver()
    {
        var logger = new RecordingLogger();
        var client = new OpenAiCompatibleClient(Http("codex->claude"), "http://cli-proxy:8090/v1/codex", "k", logger);

        var response = await client.CompleteAsync(new LlmRequest { Model = "gpt-6.2-astra", SystemPrompt = "s", UserPrompt = "u", MaxTokens = 10 }, CancellationToken.None);

        Assert.Equal("ok", response.Text);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("codex->claude", entry.Message);
        Assert.Contains("gpt-6.2-astra", entry.Message);
    }

    [Theory]
    [InlineData("claude->claude-alt", false)]
    [InlineData("claude-alt->claude", false)]
    [InlineData("claude->codex", true)]
    [InlineData("claude-alt->codex", true)]
    [InlineData("garbled", true)]
    public async Task WarnsOnlyWhenTheCliFamilyChanges(string hop, bool warns)
    {
        var logger = new RecordingLogger();
        var client = new OpenAiCompatibleClient(Http(hop), "http://cli-proxy:8090/v1/claude", "k", logger);

        await client.CompleteAsync(new LlmRequest { Model = "claude-opus-latest", SystemPrompt = "s", UserPrompt = "u", MaxTokens = 10 }, CancellationToken.None);

        Assert.Equal(warns, logger.Entries.Count == 1);
    }

    [Fact]
    public async Task StaysQuietWithoutFailover()
    {
        var logger = new RecordingLogger();
        var client = new OpenAiCompatibleClient(Http(null), "http://cli-proxy:8090/v1/codex", "k", logger);

        await client.CompleteAsync(new LlmRequest { Model = "gpt-6.2-astra", SystemPrompt = "s", UserPrompt = "u", MaxTokens = 10 }, CancellationToken.None);

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task RecordsTheFailoverOnceAsAWarningOnTheRunOfTheCurrentFlow()
    {
        var sink = new RecordingWarningSink();
        var runId = Guid.NewGuid();
        var client = new OpenAiCompatibleClient(Http("codex->claude"), "http://cli-proxy:8090/v1/codex", "k");
        var request = new LlmRequest { Model = "gpt-6.2-astra", SystemPrompt = "s", UserPrompt = "u", MaxTokens = 10 };

        using (RunWarningScope.Begin(runId, sink))
        {
            await Task.WhenAll(
                client.CompleteAsync(request, CancellationToken.None),
                Task.Run(() => client.CompleteAsync(request, CancellationToken.None)));
        }
        await client.CompleteAsync(request, CancellationToken.None);

        var warning = Assert.Single(sink.Warnings);
        Assert.Equal((runId, RunWarningSources.Llm, RunWarningCodes.LlmFailover), (warning.RunId, warning.Source, warning.Code));
        Assert.Contains("another one answered instead", warning.Message);
        Assert.DoesNotContain("codex", warning.Message);
        Assert.DoesNotContain("gpt-6.2-astra", warning.Message);
        Assert.Null(RunWarningScope.Current);
    }

    [Fact]
    public async Task ScopesOfParallelRunsDoNotMix()
    {
        var sink = new RecordingWarningSink();
        var client = new OpenAiCompatibleClient(Http("claude->codex"), "http://cli-proxy:8090/v1/claude", "k");
        var runs = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(runs.Select(runId => Task.Run(async () =>
        {
            using var _ = RunWarningScope.Begin(runId, sink);
            await client.CompleteAsync(new LlmRequest { Model = $"m-{runId}", SystemPrompt = "s", UserPrompt = "u", MaxTokens = 10 }, CancellationToken.None);
        })));

        Assert.Equal(runs.Order().ToList(), sink.Warnings.Select(w => w.RunId).Order().ToList());
    }

    private static HttpClient Http(string? failover) => new(new Handler(failover));

    private sealed class Handler(string? failover) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Ok, Encoding.UTF8, "application/json") };
            if (failover is not null)
                response.Headers.Add(OpenAiCompatibleClient.FailoverHeader, failover);
            return Task.FromResult(response);
        }
    }

    private sealed class RecordingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
