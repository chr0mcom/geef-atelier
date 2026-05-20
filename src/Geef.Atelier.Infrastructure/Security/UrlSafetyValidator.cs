using System.Net;
using System.Net.Sockets;

namespace Geef.Atelier.Infrastructure.Security;

internal sealed class UrlSafetyValidator : IUrlSafetyValidator
{
    private static readonly string[] AllowedSchemes = ["http", "https"];

    public async Task<UrlSafetyResult> ValidateAsync(Uri url, CancellationToken ct = default)
    {
        if (!AllowedSchemes.Contains(url.Scheme, StringComparer.OrdinalIgnoreCase))
            return new UrlSafetyResult(false, $"Scheme '{url.Scheme}' is not allowed. Only http and https are permitted.");

        IPAddress[] addresses;
        try
        {
            addresses = await Dns.GetHostAddressesAsync(url.Host, ct);
        }
        catch (Exception ex)
        {
            return new UrlSafetyResult(false, $"DNS resolution failed for '{url.Host}': {ex.Message}");
        }

        if (addresses.Length == 0)
            return new UrlSafetyResult(false, $"DNS returned no addresses for '{url.Host}'.");

        foreach (var address in addresses)
        {
            var blockReason = GetPrivateIpBlockReason(address);
            if (blockReason is not null)
                return new UrlSafetyResult(false, $"Resolved IP {address} for '{url.Host}' is in a blocked range: {blockReason}");
        }

        return new UrlSafetyResult(true, null);
    }

    internal static string? GetPrivateIpBlockReason(IPAddress address)
    {
        // Handle IPv4-mapped IPv6 (::ffff:x.x.x.x) — unwrap and check as IPv4
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
            return GetPrivateIpv4BlockReason(address);

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
            return GetPrivateIpv6BlockReason(address);

        return null;
    }

    private static string? GetPrivateIpv4BlockReason(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return (bytes[0], bytes[1]) switch
        {
            (10, _)                             => "10.0.0.0/8 (private)",
            (172, >= 16 and <= 31)              => "172.16.0.0/12 (private)",
            (192, 168)                          => "192.168.0.0/16 (private)",
            (127, _)                            => "127.0.0.0/8 (loopback)",
            (169, 254)                          => "169.254.0.0/16 (link-local / cloud metadata)",
            (0, _)                              => "0.0.0.0/8 (this network)",
            (100, >= 64 and <= 127)             => "100.64.0.0/10 (shared address space / CGN)",
            (>= 224, _)                         => "224.0.0.0/4 (multicast or reserved)",
            _                                   => null
        };
    }

    private static string? GetPrivateIpv6BlockReason(IPAddress address)
    {
        var bytes = address.GetAddressBytes();

        // ::1 loopback
        if (address.Equals(IPAddress.IPv6Loopback))
            return "::1 (IPv6 loopback)";

        // fc00::/7 unique local (fc00:: to fdff::)
        if ((bytes[0] & 0xFE) == 0xFC)
            return "fc00::/7 (IPv6 unique local)";

        // fe80::/10 link-local
        if (bytes[0] == 0xFE && (bytes[1] & 0xC0) == 0x80)
            return "fe80::/10 (IPv6 link-local)";

        // ff00::/8 multicast
        if (bytes[0] == 0xFF)
            return "ff00::/8 (IPv6 multicast)";

        // 64:ff9b::/96 NAT64
        if (bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B)
            return "64:ff9b::/96 (NAT64)";

        return null;
    }
}
