using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Grounding;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace Geef.Atelier.Infrastructure.Pipeline.Assembly;

/// <summary>The crew-composition pipeline: composer executor and the deterministic crew-spec validator.</summary>
internal sealed class CompositionPipelineAssembler : IPipelineAssembler
{
    /// <inheritdoc/>
    public int Priority => 100;

    /// <inheritdoc/>
    public bool CanAssemble(RunEntity run, CrewSnapshot snapshot) =>
        run.Kind == RunKind.CrewComposition || snapshot.TemplateName == SystemCrew.CrewComposerTemplateName;

    /// <inheritdoc/>
    public GeefPipelineRunner<FinalizedDocument> Assemble(PipelineAssemblyContext context)
    {
        var services = context.RunServices;
        var validatorReviewer = new CrewSpecValidatorReviewer(services.GetRequiredService<ICrewSpecValidator>());
        var composerExecutor = ActivatorUtilities.CreateInstance<CrewComposerExecutor>(services, context.Snapshot.Executor);

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
            customExecutionStep: composerExecutor,
            specialReviewerResolver: profile => profile.Name == "crew-spec-validator" ? validatorReviewer : null,
            toolBackedGroundingProvider: services.GetService<ToolBackedGroundingProvider>(),
            warningSink: context.WarningSink);
    }
}
