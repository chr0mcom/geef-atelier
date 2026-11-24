using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Geef.Atelier.Web.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Testcontainers.PostgreSql;

namespace Geef.Atelier.Tests.Web;

[Trait("Category", "Integration")]
public sealed class HealthEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();
    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task ReturnsOkWhenDbReachable()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AtelierDbContext>));
                    if (descriptor is not null) services.Remove(descriptor);

                    services.AddDbContext<AtelierDbContext>(options =>
                        options.UseNpgsql(_postgres.GetConnectionString()));
                }));

        using var client = factory.CreateClient();

        // Run migrations so health check can connect
        using (var scope = factory.Services.CreateScope())
        {
            await scope.ServiceProvider
                .GetRequiredService<AtelierDbContext>()
                .Database.MigrateAsync();
        }

        var response = await client.GetAsync("/health");
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void StartupFailsWhenTheDatabaseCannotBeMigrated()
    {
        const string badConnectionString =
            "Host=localhost;Port=19999;Database=doesnotexist;Username=nobody;Password=wrong;Timeout=3";

        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AtelierDbContext>));
                    if (descriptor is not null) services.Remove(descriptor);

                    services.AddDbContext<AtelierDbContext>(options =>
                        options.UseNpgsql(badConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
                }));

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task ReturnsUnhealthyWhenDbBecomesUnavailableAfterStart()
    {
        var connectionString = _postgres.GetConnectionString() + ";Timeout=3";
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AtelierDbContext>));
                    if (descriptor is not null) services.Remove(descriptor);

                    services.AddDbContext<AtelierDbContext>(options =>
                        options.UseNpgsql(connectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
                }));

        using var client = factory.CreateClient();
        Assert.Equal(System.Net.HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);

        await _postgres.StopAsync();

        var response = await client.GetAsync("/health");
        Assert.True(response.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task PendingMigrationsCheckIsUnhealthyUntilTheDatabaseIsMigrated()
    {
        await using var db = new AtelierDbContext(new DbContextOptionsBuilder<AtelierDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options);
        var check = new PendingMigrationsHealthCheck(db);

        var before = await check.CheckHealthAsync(new HealthCheckContext());
        await db.Database.MigrateAsync();
        var after = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Unhealthy, before.Status);
        Assert.Contains("Step47EngineHonesty", before.Description);
        Assert.Equal(HealthStatus.Healthy, after.Status);
    }
}
