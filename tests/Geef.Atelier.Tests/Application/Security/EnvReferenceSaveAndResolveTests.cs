using System.Net;
using System.Text.Json;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Application.Security;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Core.Persistence.Providers;
using Geef.Atelier.Core.Persistence.Tools;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Persistence;
using Geef.Atelier.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Application.Security;

/// <summary>
/// Environment-variable allow list end to end: saving through the real services (real Postgres) and resolving at
/// runtime. Allowed names are saved and read; other names are rejected on save and never read when they come from
/// legacy rows.
/// </summary>
[Collection("Postgres")]
public sealed class EnvReferenceSaveAndResolveTests(PostgresFixture fixture) : IDisposable
{
    private readonly string _allowed = "ATELIER_SECRET_ENVTEST_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private readonly string _forbidden = "GEEF_FORBIDDEN_ENVTEST_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
    private readonly CapturingHandler _handler = new();

    private void SetVariables()
    {
        Environment.SetEnvironmentVariable(_allowed, "allowed-secret");
        Environment.SetEnvironmentVariable(_forbidden, "forbidden-secret");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_allowed, null);
        Environment.SetEnvironmentVariable(_forbidden, null);
    }

    private ServiceProvider BuildProvider()
    {
        SetVariables();
        var services = new ServiceCollection();
        services.AddDbContext<AtelierDbContext>(opt => opt.UseNpgsql(fixture.ConnectionString)
               .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddAtelierPersistence();
        services.AddAtelierApplication();
        services.AddSingleton<IModelCatalog, PassThroughModelCatalog>();
        services.AddScoped<IKnowledgeService, NoOpKnowledgeService>();
        services.AddSingleton<IHttpClientFactory>(new AnyNameHttpClientFactory(new HttpClient(_handler)));
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static Dictionary<string, JsonElement> HttpSettings(string? apiKeyEnv, string? endpointEnv = null, string endpoint = "http://llm.test/v1") =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["endpoint"] = endpoint,
            ["api_key_env"] = apiKeyEnv,
            ["endpoint_env_override"] = endpointEnv,
            ["auth_header_name"] = "Authorization",
            ["auth_header_format"] = "Bearer {key}",
            ["models_endpoint"] = "/models",
        }.Where(kv => kv.Value is not null).ToDictionary()))!;

    private static Provider HttpProvider(string name, string? apiKeyEnv, string? endpointEnv = null) =>
        new(name, name, "test", ProviderType.Http, HttpSettings(apiKeyEnv, endpointEnv), false, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid().ToString("N")[..8]}";

    [Fact]
    public async Task Provider_AllowedName_IsSaved_AndItsKeyIsSent()
    {
        await using var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IProviderService>();

        var saved = await svc.CreateCustomAsync(HttpProvider(Unique("envok"), _allowed));
        await svc.TestConnectionAsync(saved.Name);

        Assert.Equal($"Bearer allowed-secret", _handler.LastAuthorization);
    }

    [Theory]
    [InlineData("api")]
    [InlineData("endpoint")]
    public async Task Provider_ForbiddenName_IsRejectedOnCreateAndUpdate(string field)
    {
        await using var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IProviderService>();
        var bad = field == "api" ? HttpProvider(Unique("envbad"), "CLI_PROXY_API_KEY") : HttpProvider(Unique("envbad"), null, "ConnectionStrings__DefaultConnection");

        await Assert.ThrowsAsync<EnvReferenceNotAllowedException>(() => svc.CreateCustomAsync(bad));

        var good = await svc.CreateCustomAsync(HttpProvider(Unique("envupd"), _allowed));
        await Assert.ThrowsAsync<EnvReferenceNotAllowedException>(() => svc.UpdateCustomAsync(good.Name, bad));
        var stored = await svc.GetByNameAsync(good.Name);
        Assert.Equal(_allowed, HttpProviderSettings.FromSettings(stored!.Settings).ApiKeyEnv);
    }

    [Fact]
    public async Task Provider_LegacyRowWithForbiddenName_IsNeverRead()
    {
        await using var sp = BuildProvider();
        var name = Unique("custom-legacy");
        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IProviderRepository>().CreateAsync(HttpProvider(name, _forbidden), default);

        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IProviderService>().TestConnectionAsync(name);
        Assert.Null(_handler.LastAuthorization);

        var resolver = new LlmClientResolver(
            sp.GetRequiredService<IHttpClientFactory>(),
            Options.Create(new LlmOptions()),
            sp.GetRequiredService<IServiceScopeFactory>(),
            NullLoggerFactory.Instance,
            sp.GetRequiredService<IEnvReferencePolicy>());
        var (client, model, _) = resolver.ForProfile(name, "m", null);
        try
        {
            await client.CompleteAsync(new LlmRequest { Model = model, SystemPrompt = "s", UserPrompt = "u", MaxTokens = 5 }, default);
        }
        catch (Exception)
        {
        }
        Assert.DoesNotContain("forbidden-secret", _handler.AllAuthorizations);
    }

    [Fact]
    public async Task Provider_LegacyEndpointOverrideWithForbiddenName_IsIgnored()
    {
        await using var sp = BuildProvider();
        var name = Unique("custom-legacy-ep");
        Environment.SetEnvironmentVariable(_forbidden, "http://evil.test/v1");
        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IProviderRepository>().CreateAsync(HttpProvider(name, null, _forbidden), default);

        await using (var scope = sp.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IProviderService>().TestConnectionAsync(name);

        Assert.Equal("llm.test", _handler.LastUri?.Host);
    }

    private sealed class CapturingHandler(string body = "{\"data\":[]}") : HttpMessageHandler
    {
        public string? LastAuthorization { get; private set; }
        public Uri? LastUri { get; private set; }
        public List<string> AllAuthorizations { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastAuthorization = request.Headers.TryGetValues("Authorization", out var values) ? string.Join(",", values) : null;
            if (LastAuthorization is not null)
                AllAuthorizations.Add(LastAuthorization);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class AnyNameHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
