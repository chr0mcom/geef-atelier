using System.Net;
using System.Net.Sockets;

namespace Geef.Atelier.Infrastructure.Security;

public sealed record UrlSafetyResult(bool IsAllowed, string? RejectionReason);

public interface IUrlSafetyValidator
{
    Task<UrlSafetyResult> ValidateAsync(Uri url, CancellationToken ct = default);
}
