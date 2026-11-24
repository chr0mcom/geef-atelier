using Geef.Atelier.Application.Orders;
using Geef.Atelier.Application.Sources;
using Geef.Atelier.Infrastructure.Orders;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>DI registration of source intake, extraction, file storage and cleanup.</summary>
public static class SourceServiceExtensions
{
    /// <summary>
    /// Registers the source services, including the cleanup background service and the order export. The options of
    /// section <c>Sources</c> are bound by the host (<c>Configure&lt;SourceOptions&gt;</c>).
    /// </summary>
    public static IServiceCollection AddSources(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISourceFileStore, SourceFileStore>();
        services.AddSingleton<ISourceTextExtractor, PdfSourceExtractor>();
        services.AddSingleton<ISourceTextExtractor, DocxSourceExtractor>();
        services.AddSingleton<ISourceTextExtractor, PlainTextSourceExtractor>();
        services.AddSingleton<SourceIntakeLimiter>();
        services.AddSingleton<IsolatedExtractionProcess>();
        services.AddSingleton<ISourceExtractionRunner, SourceExtractionRunner>();
        services.AddScoped<ISourceIntakeService, SourceIntakeService>();
        services.AddScoped<IOrderExportService, OrderExportService>();
        services.AddHostedService<SourceCleanupBackgroundService>();
        return services;
    }
}
