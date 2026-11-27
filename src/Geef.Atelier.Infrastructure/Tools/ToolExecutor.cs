using Geef.Atelier.Application.Security;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Tools;

/// <summary>
/// Dispatches tool invocations to the appropriate executor and persists an audit record for
/// each call. The audit record is written in its own short-lived scope and a failed write is only logged:
/// it never discards the tool result and never leaves a broken entity in the run's shared <c>DbContext</c>.
/// <list type="bullet">
///   <item><see cref="ToolType.StaticContext"/> — returns configured static text (fully wired).</item>
///   <item><see cref="ToolType.WebSearch"/> — calls Tavily API (wired in A-T9).</item>
///   <item>All other types — return a "NotYetWired" error result.</item>
/// </list>
/// When a call throws, the caller (and through it the model) only gets a fixed sentence; the exception goes to the
/// log with credential-like fragments removed (<see cref="SecretRedactionExtensions.ToRedactedLogText"/>).
/// </summary>
internal sealed class ToolExecutor(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    ILogger<ToolExecutor> logger,
    IEnvReferencePolicy? envReferencePolicy = null) : IToolExecutor
{
    private readonly IEnvReferencePolicy _envPolicy = envReferencePolicy ?? EnvReferencePolicy.Default;

    /// <summary>Error text when a tool call throws for an unexpected reason.</summary>
    internal const string ToolFailedMessage = "The tool could not be run.";

    /// <summary>Error text when the web search fails.</summary>
    internal const string WebSearchFailedMessage = "The web search failed.";

    /// <inheritdoc/>
    public async Task<ToolExecutionResult> ExecuteAsync(
        ToolDefinition tool,
        string inputJson,
        ToolInvocationContext ctx,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ToolExecutionResult result;

        try
        {
            result = tool.ToolType switch
            {
                ToolType.StaticContext => ExecuteStaticContext(tool),
                ToolType.WebSearch     => await ExecuteWebSearchAsync(tool, inputJson, ct),
                _ => new ToolExecutionResult(
                    $"Tool type '{tool.ToolType}' not yet wired in ToolExecutor. Use GroundingProvider path.",
                    null,
                    "NotYetWired")
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Tool execution failed: tool={ToolName} type={ToolType} run={RunId}: {Error}",
                tool.Name, tool.ToolType, ctx.RunId, ex.ToRedactedLogText());
            result = new ToolExecutionResult("", null, ToolFailedMessage);
        }

        sw.Stop();

        var outcome = result.Error is null
            ? ToolInvocationOutcome.Success
            : ToolInvocationOutcome.Failed;

        var invocation = new ToolInvocation
        {
            Id = Guid.NewGuid(),
            RunId = ctx.RunId,
            IterationNumber = ctx.IterationNumber,
            ActorType = ctx.ActorType,
            ActorName = ctx.ActorName,
            ToolName = tool.Name,
            ToolType = tool.ToolType,
            InputJson = inputJson,
            OutputExcerpt = result.Output.Length > 500
                ? result.Output[..500]
                : result.Output,
            CostEur = result.CostEur,
            DurationMs = (int)sw.ElapsedMilliseconds,
            Sequence = ctx.Sequence,
            Outcome = outcome,
            CreatedAt = DateTimeOffset.UtcNow
        };

        await WriteAuditAsync(invocation, ct);

        return result;
    }

    private async Task WriteAuditAsync(ToolInvocation invocation, CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var repository = scope.ServiceProvider.GetRequiredService<IToolInvocationRepository>();
            await repository.AddAsync(invocation, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Tool audit record could not be written: tool={ToolName} run={RunId}; the tool result is kept.",
                invocation.ToolName, invocation.RunId);
        }
    }

    // -------------------------------------------------------------------------
    // Concrete executors
    // -------------------------------------------------------------------------

    private static ToolExecutionResult ExecuteStaticContext(ToolDefinition tool)
    {
        var content = tool.Settings.TryGetValue(ToolDefinitionSettingsKeys.StaticContent, out var v)
            ? v
            : "";
        return new ToolExecutionResult(content, null, null);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves the secret value referenced by <see cref="ToolDefinition.SecretRef"/> from
    /// the process environment.  Returns <see langword="null"/> when no reference is set, the
    /// variable is not present, or the name is not on the allow list (<see cref="IEnvReferencePolicy"/>).
    /// </summary>
    private string? ResolveSecret(ToolDefinition tool) =>
        _envPolicy.Read(tool.SecretRef, $"tool '{tool.Name}' secret");

    // -------------------------------------------------------------------------
    // Concrete executors (A-T9)
    // -------------------------------------------------------------------------

    private async Task<ToolExecutionResult> ExecuteWebSearchAsync(
        ToolDefinition tool,
        string inputJson,
        CancellationToken ct)
    {
        var apiKey = ResolveSecret(tool);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new ToolExecutionResult("", null, "TAVILY_API_KEY environment variable is not set.");

        string query;
        try
        {
            using var doc = JsonDocument.Parse(inputJson);
            query = doc.RootElement.TryGetProperty("query", out var qElem)
                ? qElem.GetString() ?? string.Empty
                : string.Empty;
        }
        catch
        {
            query = inputJson;
        }

        if (string.IsNullOrWhiteSpace(query))
            return new ToolExecutionResult("", null, "web-search requires a non-empty 'query' input.");

        var maxResults = tool.Settings.TryGetValue(ToolDefinitionSettingsKeys.MaxResults, out var mrStr)
            && int.TryParse(mrStr, out var mr) ? mr : 5;

        var requestBody = new TavilySearchRequest(
            ApiKey: apiKey,
            Query: query,
            SearchDepth: "basic",
            IncludeAnswer: true,
            MaxResults: maxResults);

        try
        {
            var httpClient = httpClientFactory.CreateClient("tavily");
            var response = await httpClient.PostAsJsonAsync("search", requestBody, TavilyJsonOpts, ct);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<TavilySearchResponse>(TavilyJsonOpts, ct);
            if (result is null)
                return new ToolExecutionResult("", null, "Tavily returned an empty response body.");

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[Web Search Results]");
            if (!string.IsNullOrWhiteSpace(result.Answer))
            {
                sb.AppendLine("Summary:");
                sb.AppendLine(result.Answer);
                sb.AppendLine();
            }
            if (result.Results.Count > 0)
            {
                sb.AppendLine("Sources:");
                for (var i = 0; i < result.Results.Count; i++)
                {
                    var r = result.Results[i];
                    sb.AppendLine($"{i + 1}. {r.Title ?? string.Empty} ({r.Url ?? string.Empty})");
                    if (!string.IsNullOrWhiteSpace(r.Content))
                        sb.AppendLine($"   {(r.Content.Length > 300 ? r.Content[..300] + "…" : r.Content)}");
                }
            }
            sb.Append("[End of web search results]");

            // Tavily basic search costs approximately €0.004 per call (1 credit @ ~0.4 ct)
            const decimal CostEur = 0.004m;
            return new ToolExecutionResult(sb.ToString(), CostEur, null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "ToolExecutor: web-search call failed for tool={ToolName}: {Error}", tool.Name, ex.ToRedactedLogText());
            return new ToolExecutionResult("", null, WebSearchFailedMessage);
        }
    }

    // -------------------------------------------------------------------------
    // Tavily DTOs
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions TavilyJsonOpts = new(JsonSerializerDefaults.Web);

    private sealed record TavilySearchRequest(
        [property: JsonPropertyName("api_key")]      string ApiKey,
        [property: JsonPropertyName("query")]        string Query,
        [property: JsonPropertyName("search_depth")] string SearchDepth,
        [property: JsonPropertyName("include_answer")] bool IncludeAnswer,
        [property: JsonPropertyName("max_results")]  int MaxResults);

    private sealed class TavilySearchResponse
    {
        [JsonPropertyName("answer")]
        public string? Answer { get; set; }

        [JsonPropertyName("results")]
        public List<TavilyResult> Results { get; set; } = [];
    }

    private sealed class TavilyResult
    {
        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }
    }
}
