using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Tests.Web.Auth;

/// <summary>
/// The administrator from the configuration is created once and kept in sync (shared by Program.cs and the browser
/// test host), and both authorization policies exist wherever <c>AddAtelierPolicies</c> is used.
/// </summary>
public sealed class AdminUserSeedingTests
{
    [Fact]
    public async Task CreatesTheConfiguredAdministratorOnce()
    {
        var users = new InMemoryAtelierUserRepository();
        var services = Provider(users, "boss", "hash-1");

        await services.SeedAdminUserFromOptionsAsync();
        await services.SeedAdminUserFromOptionsAsync();

        var user = Assert.Single(await users.GetAllAsync(CancellationToken.None));
        Assert.Equal("boss", user.Username);
        Assert.True(user.IsAdmin);
        Assert.True(user.IsActive);
        Assert.Equal("hash-1", user.PasswordHash);
    }

    [Fact]
    public async Task RestoresPasswordAndAdminRoleOfAnExistingAccount()
    {
        var users = new InMemoryAtelierUserRepository();
        var now = DateTimeOffset.UtcNow;
        await users.AddAsync(new AtelierUser("id-1", "boss", "old", null, false, false, now, now), CancellationToken.None);

        await Provider(users, "boss", "hash-2").SeedAdminUserFromOptionsAsync();

        var user = Assert.Single(await users.GetAllAsync(CancellationToken.None));
        Assert.Equal("id-1", user.UserId);
        Assert.Equal("hash-2", user.PasswordHash);
        Assert.True(user.IsAdmin);
        Assert.True(user.IsActive);
    }

    [Fact]
    public async Task DoesNothingWithoutConfiguration()
    {
        var users = new InMemoryAtelierUserRepository();

        await Provider(users, null, null).SeedAdminUserFromOptionsAsync();

        Assert.Empty(await users.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public void BothPoliciesAreRegistered()
    {
        var options = new AuthorizationOptions().AddAtelierPolicies();

        Assert.NotNull(options.GetPolicy(AuthorizationPolicyExtensions.AdminPolicy));
        Assert.NotNull(options.GetPolicy(McpAuthorizationConstants.McpPolicy));
        Assert.Equal("AdminPolicy", AuthorizationPolicyExtensions.AdminPolicy);
    }

    private static ServiceProvider Provider(IAtelierUserRepository users, string? username, string? hash)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.ClearProviders());
        services.AddSingleton(users);
        services.AddSingleton<IOptions<AtelierUserOptions>>(Options.Create(new AtelierUserOptions { Username = username ?? "", PasswordHash = hash ?? "" }));
        return services.BuildServiceProvider();
    }
}
