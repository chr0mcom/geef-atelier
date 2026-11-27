using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>DI registration of the pipeline assemblers and their selector.</summary>
public static class PipelineAssemblyServiceExtensions
{
    /// <summary>Registers the direct, seed-draft and standard assemblers and the selector.</summary>
    public static IServiceCollection AddPipelineAssemblers(this IServiceCollection services)
    {
        services.AddSingleton<IPipelineAssembler, DirectPipelineAssembler>();
        services.AddSingleton<IPipelineAssembler, SeedDraftPipelineAssembler>();
        services.AddSingleton<IPipelineAssembler, StandardPipelineAssembler>();
        services.TryAddSingleton<IPipelineAssemblerSelector, PipelineAssemblerSelector>();
        return services;
    }
}
