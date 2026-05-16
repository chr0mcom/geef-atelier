namespace Geef.Atelier.Core.Domain.OAuth;

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
