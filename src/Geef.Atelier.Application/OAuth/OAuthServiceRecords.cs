using Geef.Atelier.Core.Domain.OAuth;

namespace Geef.Atelier.Application.OAuth;

public sealed record ClientRegistrationRequest(
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    string? LogoUri,
    string? ClientUri
);

public sealed record ClientRegistrationResult(
    string ClientId,
    DateTimeOffset ClientIdIssuedAt
);

public sealed record AuthorizationRequest(
    string ResponseType,
    string ClientId,
    string RedirectUri,
    string? Scope,
    string? State,
    string? CodeChallenge,
    string? CodeChallengeMethod
);

public sealed record AuthorizationValidationResult(
    bool IsValid,
    string? ErrorCode,
    string? ErrorDescription,
    OAuthClient? Client
);

public sealed record TokenResponse(
    string AccessToken,
    string TokenType,
    int ExpiresIn,
    string? RefreshToken,
    string Scope
);

public sealed record TokenValidationResult(
    bool IsValid,
    string? UserId,
    string? ClientId,
    string? Scope,
    DateTimeOffset? ExpiresAt
);

public sealed record ConnectedClientInfo(
    string ClientId,
    string ClientName,
    string Scope,
    DateTimeOffset IssuedAt
);
