namespace Geef.Atelier.Core.Domain.OAuth;

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
