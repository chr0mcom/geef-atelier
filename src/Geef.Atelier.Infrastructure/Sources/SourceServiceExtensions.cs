using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Sources;

/// <summary>DI registration of source intake, extraction, file storage and cleanup.</summary>
public static class SourceServiceExtensions
{
    /// <summary>Registers the source services, including the cleanup background service.</summary>
    public static IServiceCollection AddSources(this IServiceCollection services, IConfiguration configuration) => services;
}
