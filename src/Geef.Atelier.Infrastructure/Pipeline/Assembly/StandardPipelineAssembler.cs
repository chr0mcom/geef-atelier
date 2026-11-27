using Geef.Atelier.Application.Grounding;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>The classic pipeline of a crew: briefing grounding, its grounding providers, profile-based executor and reviewers, advisors.</summary>
internal sealed class StandardPipelineAssembler : IPipelineAssembler
{
    /// <inheritdoc/>
    public int Priority => 0;

    /// <inheritdoc/>
    public bool CanAssemble(RunEntity run, CrewSnapshot snapshot) => true;

    /// <inheritdoc/>
    public GeefPipelineRunner<FinalizedDocument> Assemble(PipelineAssemblyContext context)
    {
        var services = context.RunServices;
        return AtelierPipelineFactory.Build(
            context.Snapshot, context.LlmClientResolver, context.ConvergenceOptions,
            consultationRepository: services.GetRequiredService<IAdvisorConsultationRepository>(),
            runId: context.Run.Id,
            loggerFactory: context.LoggerFactory,
            additionalSinks: [context.EventSink],
            groundingProviderFactory: context.GroundingProviderFactory,
            pricingCatalog: context.PricingCatalog,
            costAccumulator: context.CostAccumulator,
            groundingRefiner: services.GetService<IGroundingRefiner>(),
            groundingConsultationRepository: services.GetService<IGroundingConsultationRepository>(),
            toolBackedGroundingProvider: services.GetService<ToolBackedGroundingProvider>(),
            warningSink: context.WarningSink,
            loopSafeguards: LoopSafeguards.From(context.ConvergenceOptions.Value));
    }
}
