namespace Geef.Atelier.Web.Endpoints;

using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Providers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

public static class ProviderEndpoints
{
    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        // Internal endpoint for CLI proxy to fetch CLI provider configs.
        // Protected by X-Internal-Token header when CliProxy:InternalApiToken is configured.
        app.MapGet("/api/internal/providers/cli", async (
            HttpContext ctx,
            IProviderService providerService,
            IConfiguration configuration,
            CancellationToken ct) =>
        {
            var expectedToken = configuration["CliProxy:InternalApiToken"];

            if (!string.IsNullOrEmpty(expectedToken))
            {
                if (!ctx.Request.Headers.TryGetValue("X-Internal-Token", out var token)
                    || token.ToString() != expectedToken)
                {
                    return Results.Unauthorized();
                }
            }

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
}

internal sealed record CliProviderDto(
    string Name,
    string DisplayName,
    Dictionary<string, JsonElement> Settings);
