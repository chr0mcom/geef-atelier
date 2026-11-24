using System.Reflection;
using Geef.Atelier.Application.Crew;
using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Application.Security;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Notifications;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Core.Persistence.Mcp;
using Geef.Atelier.Core.Persistence.Orders;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Mcp;
using Geef.Atelier.Infrastructure.Pipeline.Assembly;
using Geef.Atelier.Infrastructure.Sources;
using Geef.Atelier.Tests.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Tests.EndToEnd;

/// <summary>
/// The production wiring through <c>Program</c> (review finding A11 of Etappe 0): every class that falls back to the
/// built-in <see cref="EnvReferencePolicy.Default"/> when no policy is passed actually receives the configured
/// policy from the container, so <c>EnvReference:Allowed</c> is never silently ignored. The orchestrator requires the
/// grounding provider factory, so a missing registration fails at start instead of dropping sources silently. The
/// services of the orders and sources (Etappe 1) are registered, and the optional ones are actually injected.
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
    public async Task The_orchestrator_gets_the_grounding_and_finalizer_factories_from_the_container()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);

        Assert.NotNull(host.Services.GetRequiredService<Geef.Atelier.Web.Services.RunOrchestratorService>());
        var grounding = host.Services.GetRequiredService<IGroundingProviderFactory>();
        Assert.True(grounding.IsRegistered("vector-store"));
        Assert.True(grounding.IsRegistered("tavily"));
        Assert.NotNull(host.Services.GetRequiredService<Geef.Atelier.Application.Crew.Finalizers.IFinalizerExecutorFactory>());
    }

    [Fact]
    public async Task The_learning_switch_is_registered_once_and_off_by_default()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);

        Assert.Single(host.Services.GetServices<Microsoft.Extensions.Options.IConfigureOptions<Geef.Atelier.Core.Configuration.LearningOptions>>());
        Assert.False(host.Services.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Geef.Atelier.Core.Configuration.LearningOptions>>().CurrentValue.Enabled);
    }

    [Fact]
    public async Task The_order_and_source_services_are_registered_and_wired()
    {
        await using var host = await AtelierEndToEndHost.StartAsync(postgres);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var orderService = services.GetRequiredService<IOrderService>();
        Assert.NotNull(services.GetRequiredService<ISourceIntakeService>());
        Assert.NotNull(services.GetRequiredService<IOrderExportService>());
        Assert.NotNull(services.GetRequiredService<IOrderBriefComposer>());
        Assert.NotNull(services.GetRequiredService<IOrderRepository>());
        Assert.NotNull(services.GetRequiredService<ISourceRepository>());
        Assert.NotNull(services.GetRequiredService<IRunReportWriter>());
        var notifier = services.GetRequiredService<IOrderNotifier>();
        Assert.IsType<Geef.Atelier.Web.Notifications.LiveRunNotifier>(notifier);

        Assert.NotNull(FieldOf<ISourceIntakeService>(orderService));
        Assert.Same(notifier, FieldOf<IOrderNotifier>(orderService));

        var selector = host.Services.GetRequiredService<IPipelineAssemblerSelector>();
        Assert.NotSame(PipelineAssemblerSelector.Default, selector);
        Assert.Same(selector, FieldOf<IPipelineAssemblerSelector>(host.Services.GetRequiredService<Geef.Atelier.Web.Services.RunOrchestratorService>()));
        Assert.Equal(4, host.Services.GetServices<IPipelineAssembler>().Count());
        Assert.Single(host.Services.GetServices<IPipelineAssembler>().OfType<DirectPipelineAssembler>());

        Assert.Single(host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<SourceCleanupBackgroundService>());
    }

    private static T? FieldOf<T>(object owner) where T : class
    {
        var field = owner.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(f => f.FieldType == typeof(T));
        Assert.True(field is not null, $"{owner.GetType().Name} has no field of type {typeof(T).Name}.");
        return (T?)field!.GetValue(owner);
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
