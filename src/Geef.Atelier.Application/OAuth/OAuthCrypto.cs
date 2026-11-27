using System.Security.Cryptography;
using System.Text;

namespace Geef.Atelier.Application.OAuth;

internal static class OAuthCrypto
{
    internal static string GenerateToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    internal static string Sha256Base64Url(string input)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(input));
        return Base64UrlEncode(bytes);
    }

    internal static bool VerifyPkceS256(string codeVerifier, string codeChallenge)
    {
        var computed      = Sha256Base64Url(codeVerifier);
        var expectedBytes = Encoding.ASCII.GetBytes(codeChallenge);
        var actualBytes   = Encoding.ASCII.GetBytes(computed);
        return expectedBytes.Length == actualBytes.Length &&
               CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    internal static string HashToken(string plainToken) => Sha256Base64Url(plainToken);

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
