namespace Geef.Atelier.Core.Domain.OAuth;

/// <summary>
/// One immutable OAuth audit-trail entry (registration, consent, token issue/refresh/
/// revoke, reuse-detection). Retained permanently — the cleanup background service
/// removes expired codes/tokens but never the audit log (forensics).
/// </summary>
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
