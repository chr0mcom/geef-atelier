namespace Geef.Atelier.Core.Domain.OAuth;

public sealed record OAuthAuditLogEntry(
    Guid Id,
    string EventType,
    string? ClientId,
    string? UserId,
    string? IpAddress,
    string? UserAgent,
    string? EventDataJson,
    DateTimeOffset CreatedAt
);
