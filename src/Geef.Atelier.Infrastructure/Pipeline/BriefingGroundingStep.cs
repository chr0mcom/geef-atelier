using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal sealed class BriefingGroundingStep : IGroundingStep
{
    public Task<GroundingResult> RunAsync(string input, CancellationToken cancellationToken)
    {
        var context = new RunContext().Set(AtelierContextKeys.GroundedBrief, input);
        return Task.FromResult(new GroundingResult { Context = context, Notes = [] });
    }
}
