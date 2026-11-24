namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>Asks the assemblers by descending priority and returns the first that accepts the run.</summary>
internal sealed class PipelineAssemblerSelector(IEnumerable<IPipelineAssembler> assemblers) : IPipelineAssemblerSelector
{
    private readonly IReadOnlyList<IPipelineAssembler> _assemblers = assemblers.OrderByDescending(a => a.Priority).ToList();

    /// <summary>The four built-in assemblers with default options; used when no selector is registered.</summary>
    public static PipelineAssemblerSelector Default { get; } = new(
    [
        new CompositionPipelineAssembler(),
        new DirectPipelineAssembler(),
        new SeedDraftPipelineAssembler(),
        new StandardPipelineAssembler(),
    ]);

    /// <inheritdoc/>
    public IPipelineAssembler Select(Core.Domain.RunEntity run, Core.Domain.Crew.CrewSnapshot snapshot) =>
        _assemblers.FirstOrDefault(a => a.CanAssemble(run, snapshot))
        ?? throw new InvalidOperationException("No pipeline assembler accepts this run.");
}
