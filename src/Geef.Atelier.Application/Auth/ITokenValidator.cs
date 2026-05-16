namespace Geef.Atelier.Application.Auth;

public sealed record TokenValidationOutcome(
    bool IsValid,
    string Kind,
    string? Subject,
    string? ClientId,
    string? Scope
)
{
    public static TokenValidationOutcome Invalid { get; } = new(false, "none", null, null, null);
}

public interface ITokenValidator
{
    Task<TokenValidationOutcome> ValidateTokenAsync(string token, CancellationToken cancellationToken = default);
}
