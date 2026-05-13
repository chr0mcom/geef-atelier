using Geef.Atelier.Application.Crew;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Llm;

internal sealed class ProviderCatalog(IOptions<LlmOptions> options) : IProviderCatalog
{
    public IReadOnlyList<string> ListProviderNames() =>
        options.Value.Providers.Keys.OrderBy(k => k).ToList();
}
