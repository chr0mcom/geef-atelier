using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Providers;
using Microsoft.Extensions.Logging;
using SdkGroundingResult = Geef.Sdk.Results.GroundingResult;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Decorator over <see cref="IGroundingStep"/> that calls each configured grounding provider
/// sequentially and appends the combined enriched context to the run context.
/// When a profile has a refinement binding configured, the raw provider output is passed
/// through <see cref="IGroundingRefiner"/> before being included in the final context.
/// </summary>
internal sealed class MultiProviderGroundingStep(
    IGroundingStep inner,
    IReadOnlyList<GroundingProviderProfile> providers,
    IGroundingProviderFactory factory,
    Guid runId,
    ILogger<MultiProviderGroundingStep> logger,
    IGroundingRefiner? refiner = null,
    IGroundingConsultationRepository? consultationRepository = null) : IGroundingStep
{
    public async Task<SdkGroundingResult> RunAsync(string input, CancellationToken cancellationToken)
    {
        var innerResult = await inner.RunAsync(input, cancellationToken);

        var enrichedBlocks = new List<string>(providers.Count);
        foreach (var profile in providers)
        {
            logger.LogInformation("MultiProviderGroundingStep: run={RunId} calling provider={Provider}",
                runId, profile.Name);
            var provider = factory.Create(profile.ProviderType);

            try
            {
                var result = await LlmResilience.ExecuteAsync(
                    ct => provider.EnrichAsync(input, profile, runId, ct), cancellationToken);

                var finalResult = result;
                RefinementOutcome? refinementOutcome = null;

                if (profile.RefinementBinding is { } binding && refiner is not null)
                {
                    var config = new GroundingRefinementConfig(
                        binding, profile.RefinementMode, profile.RefinementInstructions);
                    var (refined, outcome) = await LlmResilience.ExecuteAsync(
                        ct => refiner.RefineAsync(result, input, config, profile.Name, runId, ct), cancellationToken);
                    finalResult = refined;
                    refinementOutcome = outcome;
                }

                if (finalResult.ConsultationId.HasValue && refinementOutcome is not null && consultationRepository is not null)
                {
                    try
                    {
                        await consultationRepository.UpdateRefinementOutcomeAsync(
                            finalResult.ConsultationId.Value, refinementOutcome, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex,
                            "Failed to persist refinement outcome for consultation {Id}",
                            finalResult.ConsultationId.Value);
                    }
                }

                if (!string.IsNullOrWhiteSpace(finalResult.EnrichedContext))
                    enrichedBlocks.Add(finalResult.EnrichedContext);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Genuine run cancellation / shutdown — propagate.
                throw;
            }
            catch (Exception ex)
            {
                // Grounding enrichment is optional. If a provider is unavailable even after retries,
                // skip it and continue with the remaining providers rather than aborting the run.
                logger.LogWarning(ex,
                    "MultiProviderGroundingStep: run={RunId} provider={Provider} failed and was skipped.",
                    runId, profile.Name);
            }
        }

        if (enrichedBlocks.Count == 0)
            return innerResult;

        var concatenated = string.Join("\n\n", enrichedBlocks);
        var updatedContext = innerResult.Context.Set(AtelierContextKeys.GroundingContext, concatenated);

        return new SdkGroundingResult { Context = updatedContext, Notes = innerResult.Notes };
    }
}
