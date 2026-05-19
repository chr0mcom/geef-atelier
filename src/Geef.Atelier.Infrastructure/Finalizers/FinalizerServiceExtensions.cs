using Geef.Atelier.Application.Crew.Finalizers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Finalizers;

/// <summary>DI registration for the finalizer infrastructure (executors, factory, options).</summary>
public static class FinalizerServiceExtensions
{
    public static IServiceCollection AddFinalizers(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<FinalizerOptions>(configuration.GetSection("Finalizer"));

        services.AddHttpClient("finalizer-webhook");

        services.AddSingleton<IFinalizerExecutor, FileExportFinalizerExecutor>();
        services.AddSingleton<IFinalizerExecutor, MetadataEnrichFinalizerExecutor>();
        services.AddSingleton<IFinalizerExecutor, ExternalSinkFinalizerExecutor>();
        services.AddSingleton<IFinalizerExecutor, TransformFinalizerExecutor>();
        services.AddSingleton<IFinalizerExecutorFactory, FinalizerExecutorFactory>();

        return services;
    }
}
