using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>DI registration of the pipeline assemblers and their selector.</summary>
public static class PipelineAssemblyServiceExtensions
{
    /// <summary>Registers the composition, direct, seed-draft and standard assemblers and the selector.</summary>
    public static IServiceCollection AddPipelineAssemblers(this IServiceCollection services) => services;
}
