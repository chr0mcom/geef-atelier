using System.Security.Claims;
using System.Text.Encodings.Web;
using Geef.Atelier.Application.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Auth;

internal sealed class BearerTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ITokenValidator validator)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
            return AuthenticateResult.NoResult();

        var raw = authHeader.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.Ordinal))
            return AuthenticateResult.NoResult();

        var token   = raw["Bearer ".Length..].Trim();
        var outcome = await validator.ValidateTokenAsync(token, Context.RequestAborted);

        if (!outcome.IsValid)
            return AuthenticateResult.Fail("Invalid bearer token");

        var username = outcome.Subject ?? "mcp-client";
        var isStaticBearer = outcome.Kind == "static-bearer";

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, username),
            new(ClaimTypes.NameIdentifier, outcome.ClientId ?? username),
        };

        claims.Add(isStaticBearer
            ? new Claim(ClaimTypes.Role, "admin")
            : new Claim(ClaimTypes.Role, outcome.Kind));

        if (outcome.Scope is not null)
            claims.Add(new Claim("scope", outcome.Scope));

        var identity = new ClaimsIdentity(claims, McpAuthorizationConstants.BearerScheme);
        var ticket   = new AuthenticationTicket(new ClaimsPrincipal(identity), McpAuthorizationConstants.BearerScheme);
        return AuthenticateResult.Success(ticket);
    }
}
