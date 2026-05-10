using Geef.Atelier.Infrastructure.Llm;
using Geef.Sdk.Context;

namespace Geef.Atelier.Infrastructure.Pipeline;

internal static class AtelierContextKeys
{
    public static readonly ContextKey<string>              GroundedBrief = new("geef:atelier:grounded-brief");
    public static readonly ContextKey<string>              CurrentDraft  = new("geef:atelier:current-draft");
    public static readonly ContextKey<LlmTokenUsage> TokenUsage    = new("geef:atelier:token-usage");
}
