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

        var token = raw["Bearer ".Length..].Trim();
        var ok = await validator.ValidateTokenAsync(token, Context.RequestAborted);

        if (!ok)
            return AuthenticateResult.Fail("Invalid bearer token");

        var claims   = new[] { new Claim(ClaimTypes.Name, "mcp-client") };
        var identity = new ClaimsIdentity(claims, McpAuthorizationConstants.BearerScheme);
        var ticket   = new AuthenticationTicket(
            new ClaimsPrincipal(identity),
            McpAuthorizationConstants.BearerScheme);

        return AuthenticateResult.Success(ticket);
    }
}
