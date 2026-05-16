using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Core.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Web.Endpoints;

public static class OAuthEndpoints
{
    public static IEndpointRouteBuilder MapOAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // RFC 7591 Dynamic Client Registration — anonymous
        app.MapPost("/oauth/register", async (
            [FromBody] RegisterClientRequest body,
            IOAuthService oauthService,
            IOptions<OAuthOptions> opts,
            HttpContext ctx,
            CancellationToken ct) =>
        {
            // Optional registration token protection
            var registrationToken = opts.Value.RegistrationToken;
            if (!string.IsNullOrEmpty(registrationToken))
            {
                var authHeader = ctx.Request.Headers.Authorization.ToString();
                if (!authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
                    return Results.Json(new { error = "unauthorized" }, statusCode: 401);
                var provided  = Encoding.UTF8.GetBytes(authHeader["Bearer ".Length..]);
                var expected  = Encoding.UTF8.GetBytes(registrationToken);
                if (provided.Length != expected.Length ||
                    !CryptographicOperations.FixedTimeEquals(provided, expected))
                    return Results.Json(new { error = "unauthorized" }, statusCode: 401);
            }

            if (body.RedirectUris is null || body.RedirectUris.Count == 0)
                return Results.Json(new { error = "invalid_redirect_uri", error_description = "redirect_uris is required" }, statusCode: 400);

            if (string.IsNullOrWhiteSpace(body.ClientName))
                return Results.Json(new { error = "invalid_client_metadata", error_description = "client_name is required" }, statusCode: 400);

            var request = new ClientRegistrationRequest(body.ClientName, body.RedirectUris, body.LogoUri, body.ClientUri);
            var result  = await oauthService.RegisterClientAsync(request, ct);

            ctx.Response.Headers.Location = $"/oauth/register/{result.ClientId}";
            return Results.Json(new
            {
                client_id            = result.ClientId,
                client_id_issued_at  = result.ClientIdIssuedAt.ToUnixTimeSeconds(),
                redirect_uris        = body.RedirectUris,
                client_name          = body.ClientName,
                token_endpoint_auth_method = "none",
                grant_types          = new[] { "authorization_code", "refresh_token" },
                response_types       = new[] { "code" },
            }, statusCode: 201);
        }).AllowAnonymous().DisableAntiforgery();

        // Token endpoint (authorization_code + refresh_token grants) — form-encoded, anonymous
        app.MapPost("/oauth/token", async (
            HttpContext ctx,
            IOAuthService oauthService,
            CancellationToken ct) =>
        {
            var form      = await ctx.Request.ReadFormAsync(ct);
            var grantType = form["grant_type"].ToString();

            try
            {
                if (grantType == "authorization_code")
                {
                    var code         = form["code"].ToString();
                    var clientId     = form["client_id"].ToString();
                    var redirectUri  = form["redirect_uri"].ToString();
                    var codeVerifier = form["code_verifier"].ToString();

                    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(clientId) ||
                        string.IsNullOrEmpty(redirectUri) || string.IsNullOrEmpty(codeVerifier))
                        return Results.Json(new { error = "invalid_request" }, statusCode: 400);

                    var response = await oauthService.ExchangeAuthorizationCodeAsync(code, clientId, redirectUri, codeVerifier, ct);
                    return Results.Json(new
                    {
                        access_token  = response.AccessToken,
                        token_type    = response.TokenType,
                        expires_in    = response.ExpiresIn,
                        refresh_token = response.RefreshToken,
                        scope         = response.Scope,
                    });
                }
                else if (grantType == "refresh_token")
                {
                    var refreshToken = form["refresh_token"].ToString();
                    var clientId     = form["client_id"].ToString();

                    if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(clientId))
                        return Results.Json(new { error = "invalid_request" }, statusCode: 400);

                    var response = await oauthService.RefreshTokenAsync(refreshToken, clientId, ct);
                    return Results.Json(new
                    {
                        access_token  = response.AccessToken,
                        token_type    = response.TokenType,
                        expires_in    = response.ExpiresIn,
                        refresh_token = response.RefreshToken,
                        scope         = response.Scope,
                    });
                }
                else
                {
                    return Results.Json(new { error = "unsupported_grant_type" }, statusCode: 400);
                }
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = "invalid_grant", error_description = ex.Message }, statusCode: 400);
            }
        }).AllowAnonymous().DisableAntiforgery();

        // RFC 7009 Token Revocation — form-encoded, anonymous (client validates by client_id)
        app.MapPost("/oauth/revoke", async (
            HttpContext ctx,
            IOAuthService oauthService,
            CancellationToken ct) =>
        {
            var form     = await ctx.Request.ReadFormAsync(ct);
            var token    = form["token"].ToString();
            var clientId = form["client_id"].ToString();

            if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(clientId))
                return Results.Json(new { error = "invalid_request" }, statusCode: 400);

            try
            {
                await oauthService.RevokeTokenAsync(token, clientId, ct);
                return Results.Ok();
            }
            catch (InvalidOperationException)
            {
                // RFC 7009: always return 200 even if token not found
                return Results.Ok();
            }
        }).AllowAnonymous().DisableAntiforgery();

        // Authorization endpoint — GET handled by OAuthAuthorize.razor Blazor page
        // POST here processes the Approve/Deny form
        app.MapPost("/oauth/authorize", async (
            HttpContext ctx,
            IOAuthService oauthService,
            CancellationToken ct) =>
        {
            // User must be authenticated via Cookie
            if (ctx.User.Identity?.IsAuthenticated != true)
                return Results.Redirect("/login");

            var form            = await ctx.Request.ReadFormAsync(ct);
            var action          = form["action"].ToString();       // "approve" or "deny"
            var clientId        = form["client_id"].ToString();
            var redirectUri     = form["redirect_uri"].ToString();
            var scope           = form["scope"].ToString();
            var state           = form["state"].ToString();
            var codeChallenge       = form["code_challenge"].ToString();
            var codeChallengeMethod = form["code_challenge_method"].ToString();

            // Always validate redirect_uri against registered URIs first (prevents open redirect).
            var validation = await oauthService.ValidateAuthorizationRequestAsync(
                new AuthorizationRequest("code", clientId, redirectUri, scope, state, codeChallenge, codeChallengeMethod), ct);
            if (!validation.IsValid)
                return Results.BadRequest(validation.ErrorDescription ?? "Invalid authorization request");

            if (action == "deny")
            {
                var denyUrl = $"{redirectUri}?error=access_denied";
                if (!string.IsNullOrEmpty(state)) denyUrl += $"&state={Uri.EscapeDataString(state)}";
                return Results.Redirect(denyUrl);
            }

            if (action != "approve")
                return Results.BadRequest("Invalid action");

            try
            {
                var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                             ?? ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                             ?? "stefan";

                var code = await oauthService.CreateAuthorizationCodeAsync(
                    clientId, userId, redirectUri, scope,
                    codeChallenge, codeChallengeMethod, ct);

                var callbackUrl = $"{redirectUri}?code={Uri.EscapeDataString(code)}";
                if (!string.IsNullOrEmpty(state)) callbackUrl += $"&state={Uri.EscapeDataString(state)}";
                return Results.Redirect(callbackUrl);
            }
            catch (Exception)
            {
                return Results.Redirect($"{redirectUri}?error=server_error");
            }
        })
        .RequireAuthorization(policy =>
            policy.AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
                  .RequireAuthenticatedUser())
        .DisableAntiforgery();

        return app;
    }

    private sealed record RegisterClientRequest(
        [property: JsonPropertyName("client_name")]    string? ClientName,
        [property: JsonPropertyName("redirect_uris")]  IReadOnlyList<string>? RedirectUris,
        [property: JsonPropertyName("logo_uri")]       string? LogoUri,
        [property: JsonPropertyName("client_uri")]     string? ClientUri
    );
}
