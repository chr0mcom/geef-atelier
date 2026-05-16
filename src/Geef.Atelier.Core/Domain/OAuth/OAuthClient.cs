namespace Geef.Atelier.Core.Domain.OAuth;

public sealed record OAuthClient(
    string ClientId,
    string ClientName,
    IReadOnlyList<string> RedirectUris,
    string? ClientSecretHash,
    string? LogoUri,
    string? ClientUri,
    bool IsPublic,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
