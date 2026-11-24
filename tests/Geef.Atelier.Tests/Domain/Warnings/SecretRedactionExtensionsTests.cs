using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Tests.Domain.Warnings;

/// <summary>Credential-like fragments are removed from technical texts; ordinary words, ids and model names stay.</summary>
public sealed class SecretRedactionExtensionsTests
{
    [Theory]
    [InlineData("401 Unauthorized: invalid key sk-or-v1-0123456789abcdef0123456789abcdef", "sk-or-v1-0123")]
    [InlineData("OpenAI said: Incorrect API key provided: sk-proj-AbCdEf0123456789", "sk-proj-AbCdEf")]
    [InlineData("tavily: tvly-dev-ABCDEFGHIJ0123456789 is not valid", "tvly-dev-ABCD")]
    [InlineData("google: AIzaSyA0123456789abcdefghijklmnopqrstu", "AIzaSyA01234")]
    [InlineData("header was Authorization: Bearer abc.def-ghi_jkl012345", "abc.def-ghi")]
    [InlineData("GET /search?api_key=plain-text-value&q=x", "plain-text-value")]
    [InlineData("{\"token\": \"opaque-value-123\"}", "opaque-value-123")]
    [InlineData("hash 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08 leaked", "9f86d081884c7d65")]
    public void RemovesTheSecret(string text, string secretFragment)
    {
        var redacted = text.RedactSecrets();

        Assert.DoesNotContain(secretFragment, redacted);
        Assert.Contains(SecretRedactionExtensions.Placeholder, redacted);
    }

    [Theory]
    [InlineData("The source \"Web search\" could not be read and was skipped.")]
    [InlineData("Run 3f2504e0-4f89-11d3-9a0c-0305e82c3301 failed in round 2.")]
    [InlineData("model anthropic/claude-sonnet-4-5-20250929 answered with HTTP 500")]
    [InlineData("Response status code does not indicate success: 500 (Internal Server Error).")]
    public void KeepsOrdinaryText(string text) =>
        Assert.Equal(text, text.RedactSecrets());

    [Fact]
    public void WarningDetailIsRedactedToo() =>
        Assert.DoesNotContain("sk-or-v1", "failed with sk-or-v1-0123456789abcdef0123456789abcdef".ToWarningDetail());

    [Fact]
    public void LogTextKeepsTypeAndStackButNotTheSecret()
    {
        Exception captured;
        try
        {
            throw new InvalidOperationException("outer", new HttpRequestException("upstream: key=sk-or-v1-0123456789abcdef0123456789abcdef"));
        }
        catch (Exception ex)
        {
            captured = ex;
        }

        var text = captured.ToRedactedLogText();

        Assert.Contains(typeof(InvalidOperationException).FullName!, text);
        Assert.Contains(typeof(HttpRequestException).FullName!, text);
        Assert.Contains(nameof(LogTextKeepsTypeAndStackButNotTheSecret), text);
        Assert.DoesNotContain("sk-or-v1", text);
    }
}
