using Geef.Atelier.Application.OAuth;

namespace Geef.Atelier.Tests.Application.OAuth;

public sealed class OAuthPkceTests
{
    [Fact]
    public void VerifyPkceS256_CorrectVerifier_ReturnsTrue()
    {
        const string verifier  = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = OAuthCrypto.Sha256Base64Url(verifier);

        var result = OAuthCrypto.VerifyPkceS256(verifier, challenge);

        Assert.True(result);
    }

    [Fact]
    public void VerifyPkceS256_WrongVerifier_ReturnsFalse()
    {
        const string correctVerifier = "dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk";
        var challenge = OAuthCrypto.Sha256Base64Url(correctVerifier);

        var result = OAuthCrypto.VerifyPkceS256("wrong-verifier", challenge);

        Assert.False(result);
    }

    [Fact]
    public void GenerateToken_IsDifferentEachTime()
    {
        var t1 = OAuthCrypto.GenerateToken();
        var t2 = OAuthCrypto.GenerateToken();

        Assert.NotEqual(t1, t2);
    }

    [Fact]
    public void HashToken_IsDeterministic()
    {
        const string input = "some-access-token-value";

        var hash1 = OAuthCrypto.HashToken(input);
        var hash2 = OAuthCrypto.HashToken(input);

        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void HashToken_DifferentInputs_ProduceDifferentHashes()
    {
        var hash1 = OAuthCrypto.HashToken("token-a");
        var hash2 = OAuthCrypto.HashToken("token-b");

        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void GenerateToken_IsBase64UrlSafe()
    {
        var token = OAuthCrypto.GenerateToken();

        Assert.DoesNotContain("+", token);
        Assert.DoesNotContain("/", token);
        Assert.DoesNotContain("=", token);
    }

    [Fact]
    public void Sha256Base64Url_ProducesKnownOutputForEmptyString()
    {
        // SHA-256 of "" is a well-known value
        var result = OAuthCrypto.Sha256Base64Url("");

        // SHA256("") = e3b0c44298fc1c149afb... in hex
        // Base64url of that: 47DEQpj8HBSa-_TImW-5JCeuQeRkm5NMpJWZG3hSuFU
        Assert.Equal("47DEQpj8HBSa-_TImW-5JCeuQeRkm5NMpJWZG3hSuFU", result);
    }
}
