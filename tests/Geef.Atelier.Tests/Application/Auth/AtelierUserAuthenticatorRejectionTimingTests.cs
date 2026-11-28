using System.Diagnostics;
using Geef.Atelier.Application.Auth;
using Geef.Atelier.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class AtelierUserAuthenticatorRejectionTimingTests
{
    [Theory]
    [InlineData("nobody", true)]
    [InlineData("testuser", false)]
    public async Task A_name_without_an_active_account_takes_as_long_to_reject_as_a_wrong_password(string username, bool active)
    {
        var repo = new InMemoryAtelierUserRepository();
        repo.Seed("testuser", "correct-password", isActive: active);
        var sut = new AtelierUserAuthenticator(repo, NullLogger<AtelierUserAuthenticator>.Instance);
        await sut.ValidateCredentialsAsync(username, "wrong-password");
        var hash = BCrypt.Net.BCrypt.HashPassword("reference");
        var reference = Enumerable.Range(0, 3).Min(_ =>
        {
            var single = Stopwatch.StartNew();
            BCrypt.Net.BCrypt.Verify("wrong-password", hash);
            return single.Elapsed;
        });

        var watch = Stopwatch.StartNew();
        var result = await sut.ValidateCredentialsAsync(username, "wrong-password");
        watch.Stop();

        Assert.Null(result);
        Assert.True(watch.Elapsed > reference / 2, $"Rejected after {watch.Elapsed.TotalMilliseconds:F1} ms, a password check takes {reference.TotalMilliseconds:F1} ms.");
    }
}
