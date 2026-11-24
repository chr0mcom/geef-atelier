using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.EndToEnd.Fakes;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Npgsql;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// End-to-end host over the real <c>Program</c> wiring (DI, pipeline factory, grounding step, orchestrator,
/// repositories, event sink, MCP endpoint) against an isolated database on the shared Testcontainers Postgres.
/// Only the outer boundary is replaced: the primary handlers of the named HTTP clients for the LLM endpoint,
/// the cli-proxy model listing, Tavily and embeddings. Every other named client is blocked and logged.
/// </summary>
internal sealed partial class AtelierEndToEndHost : IAsyncDisposable
{
    public const string McpToken = "e2e-mcp-token";
    public const int EmbeddingDimensions = 1536;
    public const string BlockedClientLabel = "blocked";

    private static readonly HashSet<string> FakedClients = new(StringComparer.Ordinal)
    {
        HttpClientNames.Llm,
        HttpClientNames.CliProxyModels,
        FakeTavilyEndpoint.ClientName,
        FakeEmbeddingsEndpoint.ClientName,
    };

    private static readonly RunStatus[] TerminalStatuses = [RunStatus.Completed, RunStatus.Failed, RunStatus.Aborted];

    private readonly WebApplicationFactory<Program> _factory;
    private readonly OrchestratorGate _gate;
    private readonly string _keyDirectory;

    private AtelierEndToEndHost(
        WebApplicationFactory<Program> factory,
        OrchestratorGate gate,
        string keyDirectory,
        LlmTrafficRecorder llm,
        ScriptedLlmEndpoint script,
        FakeTavilyEndpoint tavily,
        ExternalCallLog external)
    {
        _factory = factory;
        _gate = gate;
        _keyDirectory = keyDirectory;
        Llm = llm;
        Script = script;
        Tavily = tavily;
        External = external;
    }

    /// <summary>Every LLM request the application sent.</summary>
    public LlmTrafficRecorder Llm { get; }

    /// <summary>Scriptable LLM answers.</summary>
    public ScriptedLlmEndpoint Script { get; }

    /// <summary>Scriptable Tavily answers.</summary>
    public FakeTavilyEndpoint Tavily { get; }

    /// <summary>Requests to Tavily, embeddings, the model listing and blocked clients.</summary>
    public ExternalCallLog External { get; }

    /// <summary>The application's root service provider.</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>
    /// Creates an isolated database, boots the application through <c>Program</c> (which runs the
    /// migrations) and verifies that no migration is pending. The orchestrator stays held until
    /// <see cref="ReleaseOrchestrator"/> or <see cref="SubmitAsync"/>.
    /// </summary>
    public static async Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres)
    {
        var connectionString = await CreateIsolatedDatabaseAsync(postgres.ConnectionString);
        var keyDirectory = Path.Combine(Path.GetTempPath(), $"geef-e2e-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDirectory);

        var llm = new LlmTrafficRecorder();
        var external = new ExternalCallLog();
        var script = new ScriptedLlmEndpoint(llm);
        var tavily = new FakeTavilyEndpoint(external);
        var embeddings = new FakeEmbeddingsEndpoint(external, EmbeddingDimensions);
        var models = new FakeCliProxyModelsEndpoint(external);
        var gate = new OrchestratorGate();

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Development");
            web.UseSetting("hostBuilder:reloadConfigOnChange", "false");
            web.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            web.UseSetting("AtelierMcp:Token", McpToken);
            web.UseSetting("Tavily:ApiKey", "e2e-tavily-key");
            web.UseSetting("Tavily:Endpoint", "https://tavily.e2e.invalid");
            web.UseSetting("Llm:Providers:openrouter:ApiKey", "e2e-openrouter-key");
            web.UseSetting("Embeddings:Endpoint", "https://embeddings.e2e.invalid/api/v1");
            web.UseSetting("Embeddings:Dimensions", EmbeddingDimensions.ToString(System.Globalization.CultureInfo.InvariantCulture));
            web.UseSetting("CliProxy:BaseUrl", "http://cli-proxy.e2e.invalid:8090");
            web.UseSetting("ModelCatalog:WarmUpEnabled", "false");
            web.UseSetting("Orchestrator:PollingInterval", "00:00:00.150");
            web.UseSetting("Orchestrator:CancellationPollingInterval", "00:00:00.200");
            web.UseSetting("Orchestrator:MaxConcurrentRuns", "4");
            web.UseSetting("Finalizer:ExportPath", Path.Combine(keyDirectory, "exports"));
            web.UseSetting("Sources:StorageRoot", Path.Combine(keyDirectory, "sources"));
            web.UseSetting("Logging:LogLevel:Default", "Warning");

            web.ConfigureTestServices(services =>
            {
                services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyDirectory));

                var orchestratorRegistration = services.Single(d =>
                    d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(RunOrchestratorService));
                services.Remove(orchestratorRegistration);
                services.AddSingleton<RunOrchestratorService>();
                services.AddSingleton(gate);
                services.AddHostedService<HeldOrchestratorStarter>();

                services.AddHttpClient(HttpClientNames.Llm)
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(script.HandleAsync));
                services.AddHttpClient(HttpClientNames.CliProxyModels)
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(models.HandleAsync));
                services.AddHttpClient(FakeTavilyEndpoint.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(tavily.HandleAsync));
                services.AddHttpClient(FakeEmbeddingsEndpoint.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(embeddings.HandleAsync));

                services.ConfigureAll<HttpClientFactoryOptions>(options =>
                    options.HttpMessageHandlerBuilderActions.Add(builder =>
                    {
                        if (FakedClients.Contains(builder.Name ?? string.Empty))
                            return;
                        var name = builder.Name ?? string.Empty;
                        builder.PrimaryHandler = new StubHttpMessageHandler((request, _) =>
                        {
                            external.Add(new ExternalCall(
                                $"{BlockedClientLabel}:{name}", request.Method.Method,
                                request.RequestUri?.ToString() ?? string.Empty, string.Empty));
                            throw new HttpRequestException($"E2E host blocks outgoing calls of client '{name}' to {request.RequestUri}.");
                        });
                    }));
            });
        });

        var host = new AtelierEndToEndHost(factory, gate, keyDirectory, llm, script, tavily, external);
        await host.EnsureMigratedAsync();
        return host;
    }

    /// <summary>Lets the held orchestrator start polling for pending runs.</summary>
    public void ReleaseOrchestrator() => _gate.Release();

    /// <summary>Submits a run through the real <see cref="IRunService"/> and then releases the orchestrator.</summary>
    public async Task<Guid> SubmitAsync(SubmitRunRequest request)
    {
        await using var scope = Services.CreateAsyncScope();
        var runService = scope.ServiceProvider.GetRequiredService<IRunService>();
        var runId = await runService.SubmitRunAsync(request);
        ReleaseOrchestrator();
        return runId;
    }

    /// <summary>Runs <paramref name="action"/> inside a fresh DI scope.</summary>
    public async Task<T> WithScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>Runs <paramref name="query"/> against a fresh <see cref="AtelierDbContext"/>.</summary>
    public Task<T> QueryAsync<T>(Func<AtelierDbContext, Task<T>> query) =>
        WithScopeAsync(sp => query(sp.GetRequiredService<AtelierDbContext>()));

    /// <summary>
    /// Waits until the run reaches a terminal status in the database. Polls every 100 ms and fails with
    /// the recorded LLM traffic when <paramref name="timeout"/> elapses.
    /// </summary>
    public async Task<RunEntity> WaitForTerminalAsync(Guid runId, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(90));
        RunEntity? run = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            run = await QueryAsync(db => db.Runs.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId));
            if (run is not null && TerminalStatuses.Contains(run.Status) && run.Outcome is not null)
                return run;
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }

        throw new TimeoutException(
            $"Run {runId} did not reach a terminal status in time (last status: {run?.Status.ToString() ?? "missing"}).\n" +
            $"LLM traffic:\n{Llm.Describe()}");
    }

    /// <summary>
    /// Calls an MCP tool over the real <c>/mcp</c> endpoint with the static bearer token and returns the
    /// tool result parsed from its first text content block.
    /// </summary>
    public async Task<JsonElement> CallMcpToolAsync(string toolName, object arguments)
    {
        using var client = _factory.CreateClient();
        var payload = JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "tools/call",
            @params = new { name = toolName, arguments },
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", McpToken);

        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"MCP call '{toolName}' failed with {(int)response.StatusCode}: {raw}");

        var json = ExtractJsonRpcPayload(raw);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"MCP call '{toolName}' returned an error: {error}");

        var result = root.GetProperty("result");
        if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException($"MCP tool '{toolName}' reported an error: {result}");

        var text = result.GetProperty("content")[0].GetProperty("text").GetString() ?? "null";
        using var toolDoc = JsonDocument.Parse(text);
        return toolDoc.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        _gate.Release();
        await _factory.DisposeAsync();
        try
        {
            Directory.Delete(_keyDirectory, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string ExtractJsonRpcPayload(string raw)
    {
        var trimmed = raw.TrimStart();
        if (trimmed.StartsWith('{'))
            return trimmed;

        var dataLines = raw.Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line["data:".Length..].Trim())
            .Where(line => line.StartsWith('{'))
            .ToList();
        return dataLines.LastOrDefault()
               ?? throw new InvalidOperationException($"No JSON-RPC payload in MCP response: {raw}");
    }

    private async Task EnsureMigratedAsync()
    {
        var pending = await QueryAsync(async db => (await db.Database.GetPendingMigrationsAsync()).ToList());
        if (pending.Count > 0)
            throw new InvalidOperationException(
                $"Program startup left {pending.Count} migrations pending (first: {pending[0]}); the startup migration failed.");
    }

    private static async Task<string> CreateIsolatedDatabaseAsync(string serverConnectionString)
    {
        var databaseName = $"e2e_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(serverConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{databaseName}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = databaseName }.ConnectionString;
    }
}
