using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>Picks the <see cref="IPipelineAssembler"/> for a run.</summary>
internal interface IPipelineAssemblerSelector
{
    /// <summary>Returns the accepting assembler with the highest priority.</summary>
    IPipelineAssembler Select(RunEntity run, CrewSnapshot snapshot);
}
