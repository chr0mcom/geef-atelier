using Geef.Atelier.Core.Domain.OAuth;

namespace Geef.Atelier.Core.Persistence.OAuth;

public interface IOAuthAuthorizationCodeRepository
{
    Task AddAsync(OAuthAuthorizationCode code, CancellationToken ct);
    /// <summary>Finds a code by hash without consuming it (used for client_id pre-validation).</summary>
    Task<OAuthAuthorizationCode?> FindByCodeHashAsync(string codeHash, CancellationToken ct);
    /// <summary>Atomically marks the code as used (UsedAt = now). Returns false if code not found or already used.</summary>
    Task<OAuthAuthorizationCode?> ConsumeAsync(string codeHash, CancellationToken ct);
    Task DeleteExpiredAsync(CancellationToken ct);
}
