namespace Geef.Atelier.Application.Auth;

/// <summary>
/// Result of <see cref="ITokenValidator.ValidateTokenAsync"/>. Carries the
/// authenticated principal so the Bearer handler can build claims:
/// <paramref name="Kind"/> is the token source (<c>"static-bearer"</c> or
/// <c>"oauth"</c>), <paramref name="Subject"/> the user the run is attributed to,
/// <paramref name="ClientId"/> the OAuth client (null for static tokens) and
/// <paramref name="Scope"/> the granted scope (e.g. <c>"mcp:full"</c>).
/// </summary>
/// <param name="IsValid">True if the token is currently valid.</param>
/// <param name="Kind">Token source discriminator; <c>"none"</c> when invalid.</param>
/// <param name="Subject">Username the request is attributed to (run-user isolation).</param>
/// <param name="ClientId">OAuth client id, or null for the static token.</param>
/// <param name="Scope">Granted scope, or null when invalid.</param>
public sealed record TokenValidationOutcome(
    bool IsValid,
    string Kind,
    string? Subject,
    string? ClientId,
    string? Scope
)
{
    /// <summary>Shared instance representing an invalid/unauthenticated token.</summary>
    public static TokenValidationOutcome Invalid { get; } = new(false, "none", null, null, null);
}
