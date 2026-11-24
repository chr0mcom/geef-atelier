using Geef.Atelier.Application.Grounding;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>The classic pipeline of a resumed run, seeded with the last text of the run it continues.</summary>
internal sealed class SeedDraftPipelineAssembler : IPipelineAssembler
{
    /// <inheritdoc/>
    public int Priority => 50;

    /// <inheritdoc/>
    public bool CanAssemble(RunEntity run, CrewSnapshot snapshot) => run.SeedDraftText is not null;

    /// <inheritdoc/>
    public GeefPipelineRunner<FinalizedDocument> Assemble(PipelineAssemblyContext context)
    {
        var services = context.RunServices;
        return AtelierPipelineFactory.BuildWithSeedDraft(
            context.Snapshot, context.LlmClientResolver, context.ConvergenceOptions, context.Run.SeedDraftText!,
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
            warningSink: context.WarningSink);
    }
}
