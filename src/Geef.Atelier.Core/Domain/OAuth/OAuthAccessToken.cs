namespace Geef.Atelier.Core.Domain.OAuth;

public sealed record OAuthAccessToken(
    string TokenHash,
    string ClientId,
    string UserId,
    string Scope,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset CreatedAt
);
