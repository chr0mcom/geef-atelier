using System.Reflection;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Application.Security;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Persistence.Mcp;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Mcp;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The production wiring through <c>Program</c> (review finding A11 of Etappe 0): every class that falls back to the
/// built-in <see cref="EnvReferencePolicy.Default"/> when no policy is passed actually receives the configured
/// policy from the container, so <c>EnvReference:Allowed</c> is never silently ignored.
/// </summary>
[Collection(EndToEndCollection.Name)]
[Trait("Category", "EndToEnd")]
public sealed class ProductionWiringEndToEndTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Every_env_reference_consumer_uses_the_configured_policy()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        var configured = host.Services.GetRequiredService<IEnvReferencePolicy>();
        Assert.NotSame(EnvReferencePolicy.Default, configured);

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var groundingProviders = services.GetServices<IGroundingProvider>().ToList();
        var consumers = new object[]
        {
            services.GetRequiredService<ICrewService>(),
            services.GetRequiredService<IProviderService>(),
            services.GetRequiredService<ILlmClientResolver>(),
            services.GetRequiredService<IModelCatalog>(),
            services.GetRequiredService<IToolExecutor>(),
            services.GetRequiredService<IAtelierMcpClientFactory>(),
            services.GetRequiredService<IMcpServerConfigRepository>(),
            groundingProviders.OfType<RestApiGroundingProvider>().Single(),
            groundingProviders.OfType<AcademicSearchGroundingProvider>().Single(),
        };

        Assert.All(consumers, consumer => Assert.Same(configured, PolicyOf(consumer)));
    }

    [Fact]
    public async Task Run_service_and_orchestrator_get_the_warning_dependencies_from_the_container()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        await using var scope = host.Services.CreateAsyncScope();

        var runService = scope.ServiceProvider.GetRequiredService<IRunService>();

        Assert.NotNull(runService);
        Assert.NotNull(host.Services.GetRequiredService<Geef.Atelier.Web.Services.RunOrchestratorService>());
    }

    [Fact]
    public async Task The_learning_switch_is_registered_once_and_off_by_default()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);

        Assert.Single(host.Services.GetServices<Microsoft.Extensions.Options.IConfigureOptions<Geef.Atelier.Core.Configuration.LearningOptions>>());
        Assert.False(host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Geef.Atelier.Core.Configuration.LearningOptions>>().CurrentValue.Enabled);
    }

    private static IEnvReferencePolicy? PolicyOf(object consumer)
    {
        var field = consumer.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(f => typeof(IEnvReferencePolicy).IsAssignableFrom(f.FieldType));
        Assert.True(field is not null, $"{consumer.GetType().Name} has no env reference policy field.");
        return (IEnvReferencePolicy?)field!.GetValue(consumer);
    }
}
