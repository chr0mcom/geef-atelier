using Geef.Atelier.Application.Crew;
using Microsoft.Extensions.Options;

namespace Geef.Atelier.Infrastructure.Llm;

internal sealed class ProviderCatalog(IOptions<LlmOptions> options) : IProviderCatalog
{
    private static readonly IReadOnlyDictionary<string, string> DisplayNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["openrouter"]  = "OpenRouter (HTTP, pay-per-token)",
            ["claude-cli"]  = "Claude (Subscription CLI)",
            ["codex-cli"]   = "Codex (Subscription CLI)",
        };

    public IReadOnlyList<ProviderInfo> ListProviders() =>
        options.Value.Providers.Keys
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .Select(name => new ProviderInfo(name, DisplayNames.TryGetValue(name, out var display) ? display : name))
            .ToList();
}
