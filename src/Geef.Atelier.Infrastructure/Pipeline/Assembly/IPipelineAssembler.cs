using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Sdk;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>Builds the pipeline for one kind of run. The selector asks assemblers by descending <see cref="Priority"/>; the first that accepts builds it.</summary>
internal interface IPipelineAssembler
{
    /// <summary>Order in which the selector asks; higher first.</summary>
    int Priority { get; }

    /// <summary>True when this assembler builds the pipeline for <paramref name="run"/> with <paramref name="snapshot"/>.</summary>
    bool CanAssemble(RunEntity run, CrewSnapshot snapshot);

    /// <summary>Builds the pipeline runner.</summary>
    GeefPipelineRunner<FinalizedDocument> Assemble(PipelineAssemblyContext context);

    /// <summary>Called after the pipeline returned normally and the outcome is stored; writes run-kind specific results such as the report.</summary>
    Task OnRunFinishedAsync(PipelineAssemblyContext context, GeefPipelineResult<FinalizedDocument> result, CancellationToken ct) => Task.CompletedTask;
}
