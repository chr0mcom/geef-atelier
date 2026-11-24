using System.Diagnostics;
using Geef.Atelier.Core.Domain.Warnings;

namespace Geef.Atelier.Tests.Domain.Warnings;

/// <summary>
/// Credential-like fragments are removed from technical texts; ordinary words, ids and model names stay. Texts for users
/// (warnings) only lose unambiguous key forms, so long display names survive; every pattern runs in linear time.
/// </summary>
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
    [InlineData("Incorrect API key provided: abcd1234efgh5678", "abcd1234efgh5678")]
    [InlineData("Incorrect API key provided: abcd1234efgh5678. You can find your key at …", "abcd1234efgh5678")]
    [InlineData("invalid credentials: AbCd0123+/EfGh4567/IjKl89+MnOpQrSt012=", "AbCd0123+/EfGh")]
    [InlineData("signature dGhpcyBpcyBhIHNlY3JldCBrZXkgMTIzNDU2Nzg5MA== rejected", "dGhpcyBpcyBhIHNlY3JldCBrZXkg")]
    [InlineData("GET /v1/search?key=short123&q=test", "short123")]
    [InlineData("headers: Authorization: Custom-Scheme value-abc 123", "value-abc")]
    [InlineData("headers: x-api-key: zyx987wvu654", "zyx987wvu654")]
    [InlineData("{\"x-api-key\":\"zyx987wvu654\"}", "zyx987wvu654")]
    [InlineData("groq said: invalid gsk_AbCdEfGhIjKlMnOpQrSt0123", "gsk_AbCdEf")]
    public void RemovesCommonForms(string text, string secretFragment)
    {
        var redacted = text.RedactSecrets();

        Assert.DoesNotContain(secretFragment, redacted);
        Assert.Contains(SecretRedactionExtensions.Placeholder, redacted);
    }

    [Theory]
    [InlineData("The source \"custom-tavily-refined-academic-search-v2\" could not be read and was skipped.")]
    [InlineData("The check \"reviewer-for-long-academic-papers-2026-edition\" could not be completed in round 2 and was left out.")]
    [InlineData("The finishing step \"Export2024QuarterlyReportForManagementTeamA\" failed.")]
    public void KnownSecretsOnlyKeepsLongDisplayNames(string text)
    {
        Assert.Equal(text, text.RedactKnownSecrets());
        Assert.NotEqual(text, text.RedactSecrets());
    }

    [Theory]
    [InlineData("The source \"Web\" could not be read: sk-proj-0123456789abcdef.", "sk-proj-0123")]
    [InlineData("Answer was Bearer abcdefghijkl0123", "abcdefghijkl0123")]
    [InlineData("call with key=hunter2hunter2 failed", "hunter2hunter2")]
    [InlineData("tvly-ABCDEFGHIJ0123456789 rejected", "tvly-ABCDEF")]
    [InlineData("AIzaSyA0123456789abcdefghijklmnopqrstu rejected", "AIzaSyA0123")]
    [InlineData("gsk_AbCdEfGhIjKlMnOpQrSt0123 rejected", "gsk_AbCdEf")]
    [InlineData("Incorrect API key provided: abcd1234efgh5678", "abcd1234efgh5678")]
    public void KnownSecretsStillRemovesKeys(string text, string secretFragment) =>
        Assert.DoesNotContain(secretFragment, text.RedactKnownSecrets());

    [Theory]
    [InlineData("key")]
    [InlineData("key provided")]
    [InlineData("Authorization")]
    [InlineData("Bearer")]
    [InlineData("a1")]
    [InlineData("Ab1+")]
    [InlineData("x-api-key")]
    public void StaysLinearOnLongHostileInput(string unit)
    {
        var inputs = new[]
        {
            string.Concat(Enumerable.Repeat(unit + " ", 50_000)),
            unit + new string(' ', 200_000),
            unit + new string(' ', 200_000) + ":" + new string(' ', 200_000) + "x",
            string.Concat(Enumerable.Repeat(unit, 100_000)),
            string.Concat(Enumerable.Repeat(new string('a', 31) + "1!", 10_000)),
        };

        var watch = Stopwatch.StartNew();
        foreach (var input in inputs)
        {
            input.RedactSecrets();
            input.RedactKnownSecrets();
        }

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Redaction took {watch.Elapsed}.");
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
