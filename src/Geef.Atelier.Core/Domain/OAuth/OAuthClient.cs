namespace Geef.Atelier.Core.Domain.OAuth;

/// <summary>
/// A registered OAuth client (RFC 7591). Clients are public — they hold no usable
/// secret and authenticate via PKCE; <c>ClientSecretHash</c> is reserved/optional and
/// <c>IsPublic</c> is true for all MCP clients.
/// </summary>
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
