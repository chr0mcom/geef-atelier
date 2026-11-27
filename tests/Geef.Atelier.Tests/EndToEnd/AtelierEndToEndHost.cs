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
/// the cli-proxy model listing and Tavily. Every other named client is blocked and logged.
/// </summary>
internal sealed partial class AtelierEndToEndHost : IAsyncDisposable
{
    public const string McpToken = "e2e-mcp-token";
    public const string McpTokenUser = "e2e-mcp-user";
    public const string BlockedClientLabel = "blocked";

    private static readonly HashSet<string> FakedClients = new(StringComparer.Ordinal)
    {
        HttpClientNames.Llm,
        HttpClientNames.CliProxyModels,
        FakeTavilyEndpoint.ClientName,
    };

    private static readonly RunStatus[] TerminalStatuses = [RunStatus.Completed, RunStatus.Failed, RunStatus.Aborted];

    private readonly WebApplicationFactory<Program> _factory;
    private readonly OrchestratorGate _gate;
    private readonly string _keyDirectory;
    private readonly string _connectionString;
    private readonly string _serverConnectionString;

    private AtelierEndToEndHost(
        WebApplicationFactory<Program> factory,
        OrchestratorGate gate,
        string keyDirectory,
        string connectionString,
        string serverConnectionString,
        LlmTrafficRecorder llm,
        ScriptedLlmEndpoint script,
        FakeTavilyEndpoint tavily,
        ExternalCallLog external)
    {
        _factory = factory;
        _gate = gate;
        _keyDirectory = keyDirectory;
        _connectionString = connectionString;
        _serverConnectionString = serverConnectionString;
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

    /// <summary>Requests to Tavily, the model listing and blocked clients.</summary>
    public ExternalCallLog External { get; }

    /// <summary>The application's root service provider.</summary>
    public IServiceProvider Services => _factory.Services;

    /// <summary>
    /// Creates an isolated database, boots the application through <c>Program</c> (which runs the
    /// migrations) and verifies that no migration is pending. The orchestrator stays held until
    /// <see cref="ReleaseOrchestrator"/>. The intake is off (<c>Orders:IntakeMode=Off</c>) so that
    /// orders run exactly as in stage 1 and no clarifier call lands in <see cref="Llm"/>; intake tests switch it on in
    /// <paramref name="configureServices"/>, which runs last and may replace registrations for a single test. The switches of stage 3
    /// are off as well (<c>Direct:FactCheckMode=Off</c>, <c>Direct:WriterNotes=false</c>, <c>Intake:ContentAssumptions=Defaults</c>),
    /// so the direct path runs as in stage 2 unless a test configures <c>DirectOptions</c> or
    /// <c>IntakeOptions</c> there. The research of stage 4 is off too (<c>Research:Mode=Off</c>); research tests switch it on there.
    /// With <paramref name="kestrelPort"/> the application listens on that loopback port with the real Kestrel server instead of the
    /// in-memory test server, so that outside programs such as <c>curl</c> can reach it.
    /// </summary>
    public static async Task<AtelierEndToEndHost> StartAsync(PostgresFixture postgres, Action<IServiceCollection>? configureServices = null, int? kestrelPort = null)
    {
        var connectionString = await CreateIsolatedDatabaseAsync(postgres.ConnectionString);
        var keyDirectory = Path.Combine(Path.GetTempPath(), $"geef-e2e-keys-{Guid.NewGuid():N}");
        Directory.CreateDirectory(keyDirectory);

        var llm = new LlmTrafficRecorder();
        var external = new ExternalCallLog();
        var script = new ScriptedLlmEndpoint(llm);
        var tavily = new FakeTavilyEndpoint(external);
        var models = new FakeCliProxyModelsEndpoint(external);
        var gate = new OrchestratorGate();

        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web =>
        {
            web.UseEnvironment("Development");
            web.UseSetting("hostBuilder:reloadConfigOnChange", "false");
            web.UseSetting("ConnectionStrings:DefaultConnection", connectionString);
            web.UseSetting("AtelierMcp:Token", McpToken);
            web.UseSetting("AtelierMcp:StaticTokenUser", McpTokenUser);
            web.UseSetting("Tavily:ApiKey", "e2e-tavily-key");
            web.UseSetting("Tavily:Endpoint", "https://tavily.e2e.invalid");
            web.UseSetting("Llm:Providers:openrouter:ApiKey", "e2e-openrouter-key");
            web.UseSetting("CliProxy:BaseUrl", "http://cli-proxy.e2e.invalid:8090");
            web.UseSetting("ModelCatalog:WarmUpEnabled", "false");
            web.UseSetting("Orchestrator:PollingInterval", "00:00:00.150");
            web.UseSetting("Orchestrator:CancellationPollingInterval", "00:00:00.200");
            web.UseSetting("Orchestrator:MaxConcurrentRuns", "4");
            web.UseSetting("Sources:StorageRoot", Path.Combine(keyDirectory, "sources"));
            web.UseSetting("Logging:LogLevel:Default", "Warning");
            web.UseSetting("Orders:IntakeMode", "Off");
            web.UseSetting("Direct:FactCheckMode", "Off");
            web.UseSetting("Direct:WriterNotes", "false");
            web.UseSetting("Intake:ContentAssumptions", "Defaults");
            web.UseSetting("Research:Mode", "Off");

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

                configureServices?.Invoke(services);
            });
        });

        if (kestrelPort is { } port)
            factory.UseKestrel(options => options.Listen(System.Net.IPAddress.Loopback, port));

        var host = new AtelierEndToEndHost(factory, gate, keyDirectory, connectionString, postgres.ConnectionString, llm, script, tavily, external);
        await host.EnsureMigratedAsync();
        return host;
    }

    /// <summary>Lets the held orchestrator start polling for pending runs.</summary>
    public void ReleaseOrchestrator() => _gate.Release();

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
        var root = await PostMcpAsync("tools/call", new { name = toolName, arguments });
        if (root.TryGetProperty("error", out var error))
            throw new InvalidOperationException($"MCP call '{toolName}' returned an error: {error}");

        var result = root.GetProperty("result");
        if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
            throw new InvalidOperationException($"MCP tool '{toolName}' reported an error: {result}");

        var text = result.GetProperty("content")[0].GetProperty("text").GetString() ?? "null";
        using var toolDoc = JsonDocument.Parse(text);
        return toolDoc.RootElement.Clone();
    }

    /// <summary>Sends one JSON-RPC request to the real <c>/mcp</c> endpoint with the static bearer token and returns the whole response object.</summary>
    public async Task<JsonElement> PostMcpAsync(string method, object? parameters = null)
    {
        using var client = _factory.CreateClient();
        var payload = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, method, @params = parameters ?? new { } });

        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.ParseAdd("application/json, text/event-stream");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", McpToken);

        using var response = await client.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"MCP request '{method}' failed with {(int)response.StatusCode}: {raw}");

        using var doc = JsonDocument.Parse(ExtractJsonRpcPayload(raw));
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Stops the application, then closes the pooled connections of its database and drops it. Npgsql keeps idle connections
    /// of every connection string for minutes; with one database per host the shared container would otherwise run out of
    /// connections (<c>53300: too many clients</c>) in a long end-to-end collection.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _gate.Release();
        await _factory.DisposeAsync();
        await DropDatabaseAsync();
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

    private async Task DropDatabaseAsync()
    {
        await using (var connection = new NpgsqlConnection(_connectionString))
            NpgsqlConnection.ClearPool(connection);

        var databaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database;
        try
        {
            await using var server = new NpgsqlConnection(_serverConnectionString);
            await server.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{databaseName}\" WITH (FORCE)", server);
            await command.ExecuteNonQueryAsync();
        }
        catch (NpgsqlException)
        {
        }
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
