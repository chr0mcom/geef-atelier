using Geef.Atelier.Application.Sources;
using Geef.Atelier.Core.Configuration;
using Geef.Atelier.Core.Persistence.Sources;
using Geef.Atelier.Infrastructure.Persistence.Sources;
using Geef.Atelier.Infrastructure.Sources.Extraction;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>DI registration of source intake, extraction, file storage and cleanup.</summary>
public static class SourceServiceExtensions
{
    /// <summary>
    /// Registers the source services, including the cleanup background service, the upload tickets and the counting of refused
    /// files. The options of section <c>Sources</c> are bound by the host (<c>Configure&lt;SourceOptions&gt;</c>); the options of
    /// section <c>Uploads</c> are bound here from <paramref name="configuration"/>. The ticket service and the refusal log are
    /// registered whatever <c>Uploads:Tickets</c> says, so tools and endpoints can always be bound; with <c>Off</c> they refuse.
    /// </summary>
    public static IServiceCollection AddSources(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions();
        services.Configure<UploadOptions>(configuration.GetSection(UploadOptions.SectionName));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ISourceFileStore, SourceFileStore>();
        services.AddSingleton<ISourceTextExtractor, PdfSourceExtractor>();
        services.AddSingleton<ISourceTextExtractor, DocxSourceExtractor>();
        services.AddSingleton<ISourceTextExtractor, PlainTextSourceExtractor>();
        services.AddSingleton<SourceIntakeLimiter>();
        services.AddSingleton<IsolatedExtractionProcess>();
        services.AddSingleton<ISourceExtractionRunner, SourceExtractionRunner>();
        services.AddScoped<ISourceIntakeService, SourceIntakeService>();
        services.AddScoped<SourceRefusalRepository>();
        services.AddSingleton<ISourceRefusalLog, SourceRefusalLog>();
        services.AddScoped<IUploadTicketRepository, UploadTicketRepository>();
        services.AddScoped<IUploadTicketService, UploadTicketService>();
        services.AddHostedService<SourceCleanupBackgroundService>();
        return services;
    }
}
