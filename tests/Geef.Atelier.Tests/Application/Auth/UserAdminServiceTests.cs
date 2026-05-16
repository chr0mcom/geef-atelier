using Geef.Atelier.Application.Auth;
using Geef.Atelier.Application.OAuth;
using Geef.Atelier.Core.Domain.OAuth;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Fakes.OAuth;

namespace Geef.Atelier.Tests.Application.Auth;

public sealed class UserAdminServiceTests
{
    private static (UserAdminService svc, InMemoryAtelierUserRepository repo) Create()
    {
        var repo  = new InMemoryAtelierUserRepository();
        var (oauthSvc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var svc   = new UserAdminService(repo, oauthSvc);
        return (svc, repo);
    }

    [Fact]
    public async Task CreateUser_AddsUserWithHashedPassword()
    {
        var (svc, repo) = Create();

        var user = await svc.CreateUserAsync("alice", "secret", null, CancellationToken.None);

        Assert.Equal("alice", user.Username);
        Assert.True(user.IsActive);
        Assert.False(user.IsAdmin);
        var stored = await repo.FindByUsernameAsync("alice", CancellationToken.None);
        Assert.NotNull(stored);
        Assert.True(BCrypt.Net.BCrypt.Verify("secret", stored.PasswordHash));
    }

    [Fact]
    public async Task CreateUser_DuplicateUsername_Throws()
    {
        var (svc, _) = Create();
        await svc.CreateUserAsync("alice", "secret", null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.CreateUserAsync("alice", "other", null, CancellationToken.None));
    }

    [Fact]
    public async Task GetAllUsers_ReturnsAllUsers()
    {
        var (svc, repo) = Create();
        repo.Seed("bob", "pw1");
        repo.Seed("alice", "pw2");

        var users = await svc.GetAllUsersAsync(CancellationToken.None);

        Assert.Equal(2, users.Count);
        Assert.Contains(users, u => u.Username == "alice");
        Assert.Contains(users, u => u.Username == "bob");
    }

    [Fact]
    public async Task UpdateUser_ChangesUsername()
    {
        var (svc, repo) = Create();
        var original = repo.Seed("old-name", "pw");

        await svc.UpdateUserAsync(original.UserId, "new-name", null, null, true, CancellationToken.None);

        var updated = await repo.FindByUsernameAsync("new-name", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(original.UserId, updated.UserId);
    }

    [Fact]
    public async Task UpdateUser_NullPassword_KeepsExistingHash()
    {
        var (svc, repo) = Create();
        var original = repo.Seed("alice", "original-password");

        await svc.UpdateUserAsync(original.UserId, "alice", null, null, true, CancellationToken.None);

        var updated = await repo.FindByUsernameAsync("alice", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.Equal(original.PasswordHash, updated.PasswordHash);
    }

    [Fact]
    public async Task UpdateUser_NewPassword_HashesPassword()
    {
        var (svc, repo) = Create();
        var original = repo.Seed("alice", "old-password");

        await svc.UpdateUserAsync(original.UserId, "alice", "new-password", null, true, CancellationToken.None);

        var updated = await repo.FindByUsernameAsync("alice", CancellationToken.None);
        Assert.NotNull(updated);
        Assert.True(BCrypt.Net.BCrypt.Verify("new-password", updated.PasswordHash));
    }

    [Fact]
    public async Task UpdateUser_NotFound_Throws()
    {
        var (svc, _) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.UpdateUserAsync("nonexistent-id", "x", null, null, true, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteUser_RemovesUser()
    {
        var (svc, repo) = Create();
        var user = repo.Seed("alice", "pw");

        await svc.DeleteUserAsync(user.UserId, CancellationToken.None);

        var found = await repo.FindByUserIdAsync(user.UserId, CancellationToken.None);
        Assert.Null(found);
    }

    [Fact]
    public async Task DeleteUser_NotFound_Throws()
    {
        var (svc, _) = Create();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            svc.DeleteUserAsync("nonexistent-id", CancellationToken.None));
    }

    [Fact]
    public async Task UpdateUser_DeactivateActiveUser_RevokesOAuthTokens()
    {
        var repo = new InMemoryAtelierUserRepository();
        var (oauthSvc, _, _, _, _, _) = OAuthServiceFactory.Create();
        var svc = new UserAdminService(repo, oauthSvc);

        var user = repo.Seed("alice", "pw", isActive: true);

        // Issue an OAuth token for the user
        var reg  = await oauthSvc.RegisterClientAsync(
            new ClientRegistrationRequest("App", ["https://example.com/cb"], null, null),
            CancellationToken.None);
        var challenge = OAuthCrypto.Sha256Base64Url("verifier1234567890123456789012345");
        var code = await oauthSvc.CreateAuthorizationCodeAsync(
            reg.ClientId, "alice", "https://example.com/cb", "mcp:full", challenge, "S256", CancellationToken.None);
        await oauthSvc.ExchangeAuthorizationCodeAsync(
            code, reg.ClientId, "https://example.com/cb", "verifier1234567890123456789012345", CancellationToken.None);

        var tokensBefore = await oauthSvc.GetActiveTokensForUserAsync("alice", CancellationToken.None);
        Assert.NotEmpty(tokensBefore);

        await svc.UpdateUserAsync(user.UserId, "alice", null, null, isActive: false, CancellationToken.None);

        var tokensAfter = await oauthSvc.GetActiveTokensForUserAsync("alice", CancellationToken.None);
        Assert.Empty(tokensAfter);
    }
}
