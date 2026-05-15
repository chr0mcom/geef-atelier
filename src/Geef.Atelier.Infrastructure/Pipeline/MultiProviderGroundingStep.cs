using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Sdk.Providers;
using Microsoft.Extensions.Logging;
using SdkGroundingResult = Geef.Sdk.Results.GroundingResult;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Decorator over <see cref="IGroundingStep"/> that calls each configured grounding provider
/// sequentially and appends the combined enriched context to the run context.
/// </summary>
internal sealed class MultiProviderGroundingStep(
    IGroundingStep inner,
    IReadOnlyList<GroundingProviderProfile> providers,
    IGroundingProviderFactory factory,
    Guid runId,
    ILogger<MultiProviderGroundingStep> logger) : IGroundingStep
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
            var result = await provider.EnrichAsync(input, profile, runId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.EnrichedContext))
                enrichedBlocks.Add(result.EnrichedContext);
        }

        if (enrichedBlocks.Count == 0)
            return innerResult;

        var concatenated = string.Join("\n\n", enrichedBlocks);
        var updatedContext = innerResult.Context.Set(AtelierContextKeys.GroundingContext, concatenated);

        return new SdkGroundingResult { Context = updatedContext, Notes = innerResult.Notes };
    }
}
