using Geef.Atelier.Application.Runs;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Application;

[Collection("Postgres")]
public sealed class RunServiceValidatesInputsTests(PostgresFixture fixture)
{
    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AtelierDbContext>(opt => opt.UseNpgsql(fixture.ConnectionString));
        services.AddAtelierPersistence();
        services.AddAtelierApplication();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task SubmitRunAsync_EmptyBriefing_ThrowsArgumentException()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SubmitRunAsync("", "{}"));
    }

    [Fact]
    public async Task SubmitRunAsync_WhitespaceBriefing_ThrowsArgumentException()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SubmitRunAsync("   ", "{}"));
    }

    [Fact]
    public async Task SubmitRunAsync_NullConfigJson_ThrowsArgumentNullException()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
        await Assert.ThrowsAsync<ArgumentNullException>(() => svc.SubmitRunAsync("valid briefing", null!));
    }

    [Fact]
    public async Task SubmitRunAsync_InvalidJson_ThrowsArgumentException()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.SubmitRunAsync("valid briefing", "not-json"));
    }

    [Fact]
    public async Task SubmitRunAsync_EmptyConfigJson_Succeeds()
    {
        await using var provider = BuildProvider();
        await using var scope = provider.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IRunService>();
        var id = await svc.SubmitRunAsync("valid briefing", "");
        Assert.NotEqual(Guid.Empty, id);
    }
}
