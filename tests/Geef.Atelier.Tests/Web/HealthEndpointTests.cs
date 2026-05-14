using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    public async Task ReturnsUnhealthyWhenDbUnavailable()
    {
        const string badConnectionString =
            "Host=localhost;Port=19999;Database=doesnotexist;Username=nobody;Password=wrong";

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
                host.ConfigureServices(services =>
                {
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<AtelierDbContext>));
                    if (descriptor is not null) services.Remove(descriptor);

                    services.AddDbContext<AtelierDbContext>(options =>
                        options.UseNpgsql(badConnectionString));
                }));

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        // Health check must degrade to Unhealthy (503) when DB is unreachable
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
