namespace Geef.Atelier.Application.Providers;

using System.Diagnostics;
using Geef.Atelier.Core.Domain.Providers;
using Geef.Atelier.Core.Persistence.Providers;

internal sealed class ProviderService(
    IProviderRepository repository,
    IHttpClientFactory httpClientFactory) : IProviderService
{
    private const string SystemReadOnlyMessage = "System provider is read-only.";
    private const string SystemCannotBeDeletedMessage = "System providers cannot be deleted.";
    private const string LlmClientName = "llm";

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Provider>> ListAsync(bool includeInactive = false, CancellationToken ct = default)
    {
        var systemProviders = SystemProviders.ProvidersByName.Values.ToList();

        // Custom providers from DB; active-only filter applied unless caller wants all.
        var customProviders = await repository.ListAsync(includeInactive: includeInactive, ct);

        return [.. systemProviders, .. customProviders];
    }

    /// <inheritdoc/>
    public async Task<Provider?> GetByNameAsync(string name, CancellationToken ct = default)
    {
        if (SystemProviders.ProvidersByName.TryGetValue(name, out var system))
            return system;

        return await repository.GetByNameAsync(name, ct);
    }

    /// <inheritdoc/>
    public async Task<Provider> CreateCustomAsync(Provider provider, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(provider.Name))
            throw new InvalidOperationException(SystemReadOnlyMessage);

        var name = SystemProviders.EnsureCustomPrefix(provider.Name);
        var now = DateTimeOffset.UtcNow;
        var normalized = provider with { Name = name, IsSystem = false, CreatedAt = now, UpdatedAt = now };
        await repository.CreateAsync(normalized, ct);
        return normalized;
    }

    /// <inheritdoc/>
    public async Task<Provider> UpdateCustomAsync(string name, Provider provider, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(name))
            throw new InvalidOperationException(SystemReadOnlyMessage);

        var existing = await repository.GetByNameAsync(name, ct)
            ?? throw new InvalidOperationException($"Provider '{name}' not found.");

        var updated = provider with { Name = existing.Name, IsSystem = false, CreatedAt = existing.CreatedAt, UpdatedAt = DateTimeOffset.UtcNow };
        await repository.UpdateAsync(updated, ct);
        return updated;
    }

    /// <inheritdoc/>
    public async Task DeleteCustomAsync(string name, CancellationToken ct = default)
    {
        if (SystemProviders.IsSystemProviderName(name))
            throw new InvalidOperationException(SystemCannotBeDeletedMessage);

        if (await repository.IsReferencedByAnyProfileAsync(name, ct))
            throw new InvalidOperationException("Provider is referenced by profiles and cannot be deleted.");

        await repository.DeleteAsync(name, ct);
    }

    /// <inheritdoc/>
    public async Task SetActiveAsync(string name, bool isActive, CancellationToken ct = default)
    {
        // System providers are always considered active; silently skip.
        if (SystemProviders.IsSystemProviderName(name))
            return;

        await repository.SetActiveAsync(name, isActive, ct);
    }

    /// <inheritdoc/>
    public async Task<ConnectionTestResult> TestConnectionAsync(string name, CancellationToken ct = default)
    {
        var provider = await GetByNameAsync(name, ct)
            ?? throw new InvalidOperationException($"Provider '{name}' not found.");

        if (provider.Type == ProviderType.Cli)
        {
            return new ConnectionTestResult(
                Success: true,
                LatencyMs: 0,
                ErrorMessage: null,
                ResponseSample: "CLI provider — connection test not applicable");
        }

        var settings = HttpProviderSettings.FromSettings(provider.Settings);

        // Resolve endpoint, honouring optional environment-variable override.
        var endpoint = settings.EndpointEnvOverride is { Length: > 0 } envVar
            ? (Environment.GetEnvironmentVariable(envVar) ?? settings.Endpoint)
            : settings.Endpoint;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new ConnectionTestResult(
                Success: false,
                LatencyMs: 0,
                ErrorMessage: "Provider endpoint is not configured.",
                ResponseSample: null);
        }

        // Prefer /models; fall back to /chat/completions for providers without a models list.
        var testPath = settings.ModelsEndpoint is { Length: > 0 } mp ? mp : "/chat/completions";
        var testUrl = endpoint.TrimEnd('/') + testPath;

        try
        {
            var http = httpClientFactory.CreateClient(LlmClientName);

            using var request = new HttpRequestMessage(HttpMethod.Get, testUrl);

            if (settings.ApiKeyEnv is { Length: > 0 } keyEnv)
            {
                var apiKey = Environment.GetEnvironmentVariable(keyEnv) ?? string.Empty;
                if (apiKey.Length > 0)
                {
                    var headerValue = settings.AuthHeaderFormat.Replace("{key}", apiKey);
                    request.Headers.TryAddWithoutValidation(settings.AuthHeaderName, headerValue);
                }
            }

            foreach (var (header, value) in settings.DefaultHeaders)
                request.Headers.TryAddWithoutValidation(header, value);

            var sw = Stopwatch.StartNew();
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            sw.Stop();

            var sample = await TryReadSampleAsync(response, ct);

            if (response.IsSuccessStatusCode)
            {
                return new ConnectionTestResult(
                    Success: true,
                    LatencyMs: sw.ElapsedMilliseconds,
                    ErrorMessage: null,
                    ResponseSample: sample);
            }

            return new ConnectionTestResult(
                Success: false,
                LatencyMs: sw.ElapsedMilliseconds,
                ErrorMessage: $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}",
                ResponseSample: sample);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ConnectionTestResult(
                Success: false,
                LatencyMs: 0,
                ErrorMessage: ex.Message,
                ResponseSample: null);
        }
    }

    private static async Task<string?> TryReadSampleAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            return content.Length > 500 ? content[..500] : content;
        }
        catch
        {
            return null;
        }
    }
}
