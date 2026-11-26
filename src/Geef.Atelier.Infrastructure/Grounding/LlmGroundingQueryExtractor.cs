using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Core.Domain.Crew.Models;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Uses a small LLM call (the <c>GroundingQueryExtractor</c> actor) to turn a briefing
/// into one focused web-search query, or to decide that web search is not useful.
/// Fails closed (D-079): when the call fails or the answer holds no query, nothing is searched and the raw briefing is never
/// used as the query.
/// The model comes from the <see cref="ModelSlot.Helper"/> slot of the model matrix (D-066) unless
/// <c>Llm:Actors:GroundingQueryExtractor</c> overrides it explicitly.
/// </summary>
internal sealed class LlmGroundingQueryExtractor(
    ILlmClientResolver resolver,
    ILogger<LlmGroundingQueryExtractor> logger) : IGroundingQueryExtractor
{
    private const string ActorName = "GroundingQueryExtractor";
    private const string NoSearchToken = "NO_SEARCH";
    private const int MaxQueryLength = 400;
    private const int DefaultMaxTokens = 256;

    private const string SystemPrompt = """
        You convert a writing briefing into ONE focused web-search query.
        The query is sent to a web search engine to gather current, factual context for a writer.

        Rules:
        - Respond with ONLY the search query, on a single line. No quotes, no label, no explanation.
        - Reduce the briefing to its core factual topic. Drop instructions about tone, length,
          format, language, or audience — those are not searchable.
        - If the briefing is a pure reasoning, mathematics, logic, opinion, or creative-writing
          task that does NOT benefit from current web information, respond with exactly: NO_SEARCH
        """;

    public async Task<GroundingQuery> ExtractAsync(string briefingText, CancellationToken ct)
    {
        try
        {
            var (client, model, maxTokens) = ResolveActor();

            var response = await client.CompleteAsync(new LlmRequest
            {
                Model        = model,
                SystemPrompt = SystemPrompt,
                UserPrompt   = briefingText,
                MaxTokens    = maxTokens,
            }, ct);

            var text = response.Text.Trim();

            if (text.StartsWith(NoSearchToken, StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Grounding query extraction: web search skipped (briefing not search-worthy).");
                return new GroundingQuery(ShouldSearch: false, Query: string.Empty);
            }

            var query = text.Split('\n', 2)[0].Trim().Trim('"').Trim();
            if (query.Length > MaxQueryLength)
                query = query[..MaxQueryLength];

            if (string.IsNullOrWhiteSpace(query))
            {
                logger.LogWarning("Grounding query extraction returned no query; nothing is searched.");
                return GroundingQuery.NotFormed;
            }

            logger.LogInformation("Grounding query extraction: refined query='{Query}'", query);
            return new GroundingQuery(ShouldSearch: true, Query: query);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Grounding query extraction failed; nothing is searched.");
            return GroundingQuery.NotFormed;
        }
    }

    private (ILlmClient Client, string Model, int MaxTokens) ResolveActor()
    {
        try
        {
            return resolver.ForActor(ActorName);
        }
        catch (InvalidOperationException)
        {
            var helper = ModelMatrix.Primary(ModelSlot.Helper);
            return resolver.ForProfile(helper.Provider, helper.Model, DefaultMaxTokens);
        }
    }
}
