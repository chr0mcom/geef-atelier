using Geef.Atelier.Core.Domain;
using Geef.Sdk.Context;
using Geef.Sdk.Providers;
using Geef.Sdk.Results;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal sealed class MarkdownFinalizer : IFinalizer<FinalizedDocument>
{
    public Task<FinalizeResult<FinalizedDocument>> FinalizeAsync(IRunContext context, CancellationToken cancellationToken)
    {
        var markdown = context.GetRequired(AtelierContextKeys.CurrentDraft);
        var iter     = context.GetRequired(GeefKeys.CurrentIteration);

        var document = new FinalizedDocument
        {
            Markdown       = markdown,
            IterationCount = iter
        };

        return Task.FromResult(new FinalizeResult<FinalizedDocument>
        {
            Output       = document,
            FinalContext = context,
            Summary      = $"Finalized after {iter} iteration(s)."
        });
    }
}
