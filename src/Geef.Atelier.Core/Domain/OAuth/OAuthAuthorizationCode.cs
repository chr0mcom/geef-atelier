namespace Geef.Atelier.Core.Domain.OAuth;

/// <summary>
/// A single-use authorization code (≈10-minute lifetime). Only the SHA-256
/// <c>CodeHash</c> is stored. Bound to client, redirect URI and PKCE challenge;
/// <c>UsedAt</c> is set on first exchange to enforce single use.
/// </summary>
public sealed record OAuthAuthorizationCode(
    string CodeHash,
    string ClientId,
    string UserId,
    string RedirectUri,
    string Scope,
    string CodeChallenge,
    string CodeChallengeMethod,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset CreatedAt
);
