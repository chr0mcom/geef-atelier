using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Grounding step for resume runs. Sets the grounded brief and injects the seed draft
/// (last iteration's ArtifactText) into the run context so ProfileBasedExecutor can
/// use it on iteration 1 instead of generating from scratch.
/// </summary>
internal sealed class SeedDraftGroundingStep(string seedDraftText) : IGroundingStep
{
    public Task<GroundingResult> RunAsync(string input, CancellationToken cancellationToken)
    {
        var context = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, input)
            .Set(AtelierContextKeys.SeedDraft, seedDraftText);

        return Task.FromResult(new GroundingResult { Context = context, Notes = [] });
    }
}
