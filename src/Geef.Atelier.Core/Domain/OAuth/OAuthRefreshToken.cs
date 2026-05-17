namespace Geef.Atelier.Core.Domain.OAuth;

/// <summary>
/// A persisted refresh token (≈30-day lifetime, rotated on every use). Only the
/// SHA-256 <c>TokenHash</c> is stored. <c>UsedAt</c> marks consumption; re-submitting
/// a used token triggers theft-detection (revoke all user tokens, RFC 6819).
/// </summary>
public sealed record OAuthRefreshToken(
    string TokenHash,
    string ClientId,
    string UserId,
    string Scope,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt
);
