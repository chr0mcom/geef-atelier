using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>DI registration of the pipeline assemblers and their selector.</summary>
public static class PipelineAssemblyServiceExtensions
{
    /// <summary>Registers the direct assembler and the selector.</summary>
    public static IServiceCollection AddPipelineAssemblers(this IServiceCollection services)
    {
        services.AddSingleton<IPipelineAssembler, DirectPipelineAssembler>();
        services.TryAddSingleton<IPipelineAssemblerSelector, PipelineAssemblerSelector>();
        return services;
    }
}
