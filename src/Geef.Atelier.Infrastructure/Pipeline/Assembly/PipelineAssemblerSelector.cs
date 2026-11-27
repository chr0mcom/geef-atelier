namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>
/// Asks the assemblers by descending priority and returns the first that accepts the run. A run that none accepts was set up for a way
/// of working that no longer exists (the old review loop, a composed or a learning run, D-091): it fails with
/// <see cref="Core.Domain.Crew.InvalidCrewSnapshotException"/> and the plain sentence <see cref="RetiredSetupMessage"/>.
/// </summary>
internal sealed class PipelineAssemblerSelector(IEnumerable<IPipelineAssembler> assemblers) : IPipelineAssemblerSelector
{
    /// <summary>The error of a run that no assembler accepts.</summary>
    public const string RetiredSetupMessage =
        "This task was set up for a way of working that no longer exists, so it cannot run. Please start it again.";

    private readonly IReadOnlyList<IPipelineAssembler> _assemblers = assemblers.OrderByDescending(a => a.Priority).ToList();

    /// <inheritdoc/>
    public IPipelineAssembler Select(Core.Domain.RunEntity run, Core.Domain.Crew.CrewSnapshot snapshot) =>
        _assemblers.FirstOrDefault(a => a.CanAssemble(run, snapshot))
        ?? throw new Core.Domain.Crew.InvalidCrewSnapshotException(RetiredSetupMessage);
}
