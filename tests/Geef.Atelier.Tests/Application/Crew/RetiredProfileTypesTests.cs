using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Knowledge;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain.Crew.Finalizers;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Tests.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.Application.Crew;

/// <summary>
/// Stage 8c (D-089), through the real services and real Postgres: grounding and finalizer profiles of a retired type can no
/// longer be saved, while stored rows of such a type stay in the database untouched; finalizer listings leave them out.
/// </summary>
[Collection("Postgres")]
public sealed class RetiredProfileTypesTests(PostgresFixture fixture)
{
    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AtelierDbContext>(opt => opt.UseNpgsql(fixture.ConnectionString)
               .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
        services.AddAtelierPersistence();
        services.AddAtelierApplication();
        services.AddSingleton<IModelCatalog, PassThroughModelCatalog>();
        services.AddScoped<IKnowledgeService, NoOpKnowledgeService>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid().ToString("N")[..8]}";

    [Fact]
    public async Task A_grounding_profile_of_a_retired_type_is_rejected_on_create_and_update()
    {
        await using var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var crew = scope.ServiceProvider.GetRequiredService<ICrewService>();

        foreach (var retired in GroundingProviderTypes.Retired)
        {
            var profile = new GroundingProviderProfile(Unique("custom-retired-"), "R", "d", retired,
                new Dictionary<string, string> { ["apiKeyEnv"] = "CLI_PROXY_API_KEY" }, null, false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => crew.CreateCustomGroundingProviderProfileAsync(profile));
            await Assert.ThrowsAsync<InvalidOperationException>(() => crew.UpdateCustomGroundingProviderProfileAsync(profile));
            Assert.Null(await crew.GetGroundingProviderProfileAsync(profile.Name));
        }
    }

    [Fact]
    public async Task A_stored_sink_finalizer_stays_in_the_database_but_is_neither_listed_nor_saved_again()
    {
        await using var sp = BuildProvider();
        await using var scope = sp.CreateAsyncScope();
        var crew = scope.ServiceProvider.GetRequiredService<ICrewService>();
        var repo = scope.ServiceProvider.GetRequiredService<IFinalizerProfileRepository>();
        var name = Unique("custom-old-sink-");
        var stored = new FinalizerProfile(name, "Old sink", "d", (FinalizerType)2,
            new Dictionary<string, string> { ["SinkKind"] = "webhook", ["Url"] = "https://hook.example" }, false,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        await repo.CreateAsync(stored, CancellationToken.None);

        var listed = await crew.ListFinalizerProfilesAsync();
        var customOnly = await crew.ListFinalizerProfilesAsync(includeSystem: false);
        var read = await crew.GetFinalizerProfileAsync(name);

        Assert.DoesNotContain(listed, p => p.Name == name);
        Assert.DoesNotContain(customOnly, p => p.Name == name);
        Assert.NotNull(read);
        Assert.True(read!.FinalizerType.IsRetired());
        await Assert.ThrowsAsync<InvalidOperationException>(() => crew.UpdateCustomFinalizerProfileAsync(read));
        await Assert.ThrowsAsync<InvalidOperationException>(() => crew.CreateCustomFinalizerProfileAsync(read with { Name = Unique("custom-new-sink-") }));
        Assert.NotNull(await repo.GetByNameAsync(name, CancellationToken.None));
    }
}
