using Geef.Atelier.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Endpoints;

public static class WellKnownEndpoints
{
    public static IEndpointRouteBuilder MapWellKnownEndpoints(this IEndpointRouteBuilder app)
    {
        // RFC 8414 Authorization Server Metadata
        app.MapGet("/.well-known/oauth-authorization-server", (IOptions<OAuthOptions> opts) =>
        {
            var issuer = opts.Value.Issuer;
            return Results.Json(new
            {
                issuer,
                authorization_endpoint           = $"{issuer}/oauth/authorize",
                token_endpoint                   = $"{issuer}/oauth/token",
                registration_endpoint            = $"{issuer}/oauth/register",
                response_types_supported         = new[] { "code" },
                revocation_endpoint              = $"{issuer}/oauth/revoke",
                grant_types_supported            = new[] { "authorization_code", "refresh_token" },
                code_challenge_methods_supported = new[] { "S256" },
                token_endpoint_auth_methods_supported = new[] { "none" },
                scopes_supported                 = new[] { "mcp:full" },
            });
        }).AllowAnonymous();

        // MCP Resource Server Metadata
        app.MapGet("/.well-known/oauth-protected-resource", (IOptions<OAuthOptions> opts) =>
        {
            var issuer = opts.Value.Issuer;
            return Results.Json(new
            {
                resource                  = $"{issuer}/mcp",
                authorization_servers     = new[] { issuer },
                bearer_methods_supported  = new[] { "header" },
                scopes_supported          = new[] { "mcp:full" },
            });
        }).AllowAnonymous();

        return app;
    }
}
