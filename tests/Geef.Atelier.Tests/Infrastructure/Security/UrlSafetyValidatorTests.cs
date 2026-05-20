using System.Net;
using Geef.Atelier.Infrastructure.Security;

namespace Geef.Atelier.Tests.Infrastructure.Security;

/// <summary>
/// Tests for <see cref="UrlSafetyValidator"/>. Security-critical: exhaustive coverage of
/// forbidden schemes, private IPv4/IPv6, IPv4-mapped IPv6, and DNS failure.
///
/// Most IP-range tests bypass DNS by calling the internal
/// <c>GetPrivateIpBlockReason</c> helper directly or by constructing
/// <see cref="Uri"/> objects with numeric host literals so the validator
/// resolves the IP via <c>Dns.GetHostAddressesAsync</c> without external I/O.
/// </summary>
public sealed class UrlSafetyValidatorTests
{
    private static UrlSafetyValidator CreateValidator() => new();

    // ── Scheme validation (via full ValidateAsync with numeric IP) ────────────

    [Theory]
    [InlineData("http://93.184.216.34")]    // example.com — known public IP
    [InlineData("https://93.184.216.34")]
    public async Task ValidateAsync_AllowedSchemes_AllowsPublicIp(string url)
    {
        var validator = CreateValidator();
        var uri = new Uri(url);
        var result = await validator.ValidateAsync(uri, CancellationToken.None);
        Assert.True(result.IsAllowed, result.RejectionReason);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://files.example.com")]
    public async Task ValidateAsync_ForbiddenSchemes_Blocks(string url)
    {
        var validator = CreateValidator();
        var uri = new Uri(url);
        var result = await validator.ValidateAsync(uri, CancellationToken.None);
        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RejectionReason);
    }

    // ── Private IPv4 — tested via ValidateAsync with numeric-IP URIs ─────────

    [Theory]
    [InlineData("http://10.0.0.1")]
    [InlineData("http://10.255.255.255")]
    [InlineData("http://172.16.0.1")]
    [InlineData("http://172.31.255.255")]
    [InlineData("http://192.168.0.1")]
    [InlineData("http://192.168.255.255")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://127.255.255.255")]
    [InlineData("http://169.254.0.1")]         // link-local
    [InlineData("http://169.254.169.254")]     // AWS metadata
    [InlineData("http://100.64.0.1")]          // CGN (shared address space)
    [InlineData("http://0.0.0.1")]             // 0.0.0.0/8 — this-network
    [InlineData("http://224.0.0.1")]           // multicast
    [InlineData("http://240.0.0.1")]           // reserved (>= 224 bucket)
    public async Task ValidateAsync_PrivateIPv4_Blocks(string url)
    {
        var validator = CreateValidator();
        var uri = new Uri(url);
        var result = await validator.ValidateAsync(uri, CancellationToken.None);
        Assert.False(result.IsAllowed, $"Expected {url} to be blocked");
        Assert.NotNull(result.RejectionReason);
    }

    // ── Private IPv6 — tested via ValidateAsync with bracket-notation URIs ───

    [Theory]
    [InlineData("http://[::1]")]          // loopback
    [InlineData("http://[fc00::1]")]      // unique local fc00::/7
    [InlineData("http://[fd00::1]")]      // unique local fd00::/7
    [InlineData("http://[fe80::1]")]      // link-local fe80::/10
    [InlineData("http://[ff00::1]")]      // multicast ff00::/8
    public async Task ValidateAsync_PrivateIPv6_Blocks(string url)
    {
        var validator = CreateValidator();
        var uri = new Uri(url);
        var result = await validator.ValidateAsync(uri, CancellationToken.None);
        Assert.False(result.IsAllowed, $"Expected {url} to be blocked");
        Assert.NotNull(result.RejectionReason);
    }

    // ── IPv4-mapped IPv6 (::ffff:x.x.x.x) ───────────────────────────────────

    [Theory]
    [InlineData("http://[::ffff:10.0.0.1]")]
    [InlineData("http://[::ffff:192.168.1.1]")]
    [InlineData("http://[::ffff:127.0.0.1]")]
    public async Task ValidateAsync_IPv4MappedPrivate_Blocks(string url)
    {
        var validator = CreateValidator();
        var uri = new Uri(url);
        var result = await validator.ValidateAsync(uri, CancellationToken.None);
        Assert.False(result.IsAllowed, $"Expected IPv4-mapped {url} to be blocked");
        Assert.NotNull(result.RejectionReason);
    }

    // ── DNS failure ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateAsync_InvalidDnsName_Blocks()
    {
        var validator = CreateValidator();
        var uri = new Uri("http://this-hostname-does-not-exist-xyz-123456789.invalid");
        var result = await validator.ValidateAsync(uri, CancellationToken.None);
        Assert.False(result.IsAllowed);
        Assert.NotNull(result.RejectionReason);
    }

    // ── GetPrivateIpBlockReason unit tests (bypass DNS entirely) ─────────────

    [Theory]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.255")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.255.255")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    public void GetPrivateIpBlockReason_PrivateIPv4_ReturnsReason(string ip)
    {
        var address = IPAddress.Parse(ip);
        var reason = UrlSafetyValidator.GetPrivateIpBlockReason(address);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    [InlineData("ff00::1")]
    public void GetPrivateIpBlockReason_PrivateIPv6_ReturnsReason(string ip)
    {
        var address = IPAddress.Parse(ip);
        var reason = UrlSafetyValidator.GetPrivateIpBlockReason(address);
        Assert.NotNull(reason);
    }

    [Theory]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:192.168.1.1")]
    [InlineData("::ffff:127.0.0.1")]
    public void GetPrivateIpBlockReason_IPv4MappedPrivate_ReturnsReason(string ip)
    {
        var address = IPAddress.Parse(ip);
        var reason = UrlSafetyValidator.GetPrivateIpBlockReason(address);
        Assert.NotNull(reason);
    }

    [Fact]
    public void GetPrivateIpBlockReason_PublicIPv4_ReturnsNull()
    {
        var address = IPAddress.Parse("93.184.216.34"); // example.com
        var reason = UrlSafetyValidator.GetPrivateIpBlockReason(address);
        Assert.Null(reason);
    }

    [Fact]
    public void GetPrivateIpBlockReason_PublicIPv6_ReturnsNull()
    {
        var address = IPAddress.Parse("2606:2800:21f:cb07:6820:80da:af6b:8b2c"); // example.com IPv6
        var reason = UrlSafetyValidator.GetPrivateIpBlockReason(address);
        Assert.Null(reason);
    }
}
