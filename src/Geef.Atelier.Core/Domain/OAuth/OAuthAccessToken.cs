namespace Geef.Atelier.Core.Domain.OAuth;

/// <summary>
/// A persisted OAuth access token. Only the SHA-256 <c>TokenHash</c> is stored, never
/// the token itself. Valid until <c>ExpiresAt</c> unless <c>RevokedAt</c> is set.
/// Bound to a client and the authorizing user.
/// </summary>
public sealed record OAuthAccessToken(
    string TokenHash,
    string ClientId,
    string UserId,
    string Scope,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt
);
