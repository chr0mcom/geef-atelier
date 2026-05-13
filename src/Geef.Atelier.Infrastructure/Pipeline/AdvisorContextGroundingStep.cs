using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

/// <summary>
/// Grounding step variant used for convergence-failure recovery runs. In addition to setting the
/// grounded brief it injects a pre-formed advisor context block into the initial run context so
/// that <c>ProfileBasedExecutor</c> can prepend it to the user prompt on the first iteration.
/// </summary>
internal sealed class AdvisorContextGroundingStep(string advisorContextBlock) : IGroundingStep
{
    public Task<GroundingResult> RunAsync(string input, CancellationToken cancellationToken)
    {
        var context = new RunContext()
            .Set(AtelierContextKeys.GroundedBrief, input)
            .Set(AtelierContextKeys.AdvisorBlock, advisorContextBlock);

        return Task.FromResult(new GroundingResult { Context = context, Notes = [] });
    }
}
