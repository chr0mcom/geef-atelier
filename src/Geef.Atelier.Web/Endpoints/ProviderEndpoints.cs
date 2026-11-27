namespace Geef.Atelier.Web.Endpoints;

using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

public static class ProviderEndpoints
{
    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        // Internal endpoint for CLI proxy to fetch CLI provider configs.
        // Needs the X-Internal-Token header; without a configured CliProxy:InternalApiToken nobody gets an answer.
        app.MapGet("/api/internal/providers/cli", async (
            HttpContext ctx,
            IProviderService providerService,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            if (!IsInternalCaller(ctx.Request, configuration["CliProxy:InternalApiToken"]))
                return Results.Unauthorized();

            var allProviders = await providerService.ListAsync(includeInactive: false, ct);
            var cliProviders = allProviders
                .Where(p => p.Type == ProviderType.Cli && p.IsActive)
                .Select(p => new CliProviderDto(
                    p.Name,
                    p.DisplayName,
                    p.Settings))
                .ToList();

            return Results.Ok(cliProviders);
        }).AllowAnonymous();

        return app;
    }

    private static bool IsInternalCaller(HttpRequest request, string? expectedToken) =>
        !string.IsNullOrWhiteSpace(expectedToken)
        && request.Headers.TryGetValue("X-Internal-Token", out var sent)
        && CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(sent.ToString())),
            SHA256.HashData(Encoding.UTF8.GetBytes(expectedToken)));
}

internal sealed record CliProviderDto(
    string Name,
    string DisplayName,
    Dictionary<string, JsonElement> Settings);
