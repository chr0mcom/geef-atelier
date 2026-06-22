using System.Text;
using System.Text.Json;
using Geef.Atelier.Application.Grounding;
using Geef.Atelier.Application.Pricing;
using Geef.Atelier.Application.Providers;
using Geef.Atelier.Core.Domain.Crew;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Persistence.Crew;
using Geef.Atelier.Infrastructure.Llm;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

internal sealed class GroundingRefiner(
    ILlmClientResolver llmClientResolver,
    IPricingCatalog pricingCatalog,
    IGroundingActorCostRepository costRepository,
    IServiceScopeFactory scopeFactory,
    ILogger<GroundingRefiner> logger) : IGroundingRefiner
{
    private const int MaxRefinementSources = 20;

    private static readonly LlmTool FilterTool = new()
    {
        Name = "submit_refinement",
        Description = "Submit filter decisions for each grounding source.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>("""
            {
                "type": "object",
                "properties": {
                    "sources": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "index":           { "type": "integer" },
                                "keep":            { "type": "boolean" },
                                "reason":          { "type": "string" },
                                "cleaned_snippet": { "type": "string" }
                            },
                            "required": ["index", "keep", "reason"]
                        }
                    }
                },
                "required": ["sources"]
            }
            """)
    };

    private static readonly LlmTool SynthesizeTool = new()
    {
        Name = "submit_refinement",
        Description = "Submit the synthesized grounding context and source attribution.",
        InputSchema = JsonSerializer.Deserialize<JsonElement>("""
            {
                "type": "object",
                "properties": {
                    "synthesized_text":   { "type": "string" },
                    "referenced_indices": { "type": "array", "items": { "type": "integer" } },
                    "dropped_indices":    { "type": "array", "items": { "type": "integer" } },
                    "drop_reasons":       { "type": "object", "additionalProperties": { "type": "string" } }
                },
                "required": ["synthesized_text", "referenced_indices"]
            }
            """)
    };

    public async Task<(GroundingResult Refined, RefinementOutcome Outcome)> RefineAsync(
        GroundingResult raw,
        string briefing,
        GroundingRefinementConfig config,
        string groundingProviderName,
        Guid runId,
        CancellationToken ct)
    {
        if (raw.Citations.Count == 0)
        {
            return (raw, new RefinementOutcome(
                RefinedCitations: raw.Citations,
                DroppedCitations: [],
                SynthesizedText: null,
                WasSkipped: true,
                SkipReason: "No citations to refine."));
        }

        // Validate provider via a short-lived scope (IProviderService is scoped).
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var providerService = scope.ServiceProvider.GetRequiredService<IProviderService>();
            var provider = await providerService.GetByNameAsync(config.Binding.Provider, ct);
            if (provider is null || !provider.IsActive)
            {
                logger.LogWarning(
                    "GroundingRefiner: provider '{Provider}' not found or inactive; skipping refinement for '{GroundingProvider}'.",
                    config.Binding.Provider, groundingProviderName);
                return (raw, new RefinementOutcome(
                    RefinedCitations: raw.Citations,
                    DroppedCitations: [],
                    SynthesizedText: null,
                    WasSkipped: true,
                    SkipReason: $"Refinement provider '{config.Binding.Provider}' not found or inactive."));
            }
        }

        // Enforce the hard cap: only send first MaxRefinementSources to the LLM.
        var sourcesToRefine = raw.Citations.Count > MaxRefinementSources
            ? raw.Citations.Take(MaxRefinementSources).ToList()
            : (IReadOnlyList<SourceCitation>)raw.Citations;
        var overflow = raw.Citations.Count > MaxRefinementSources
            ? raw.Citations.Skip(MaxRefinementSources).ToList()
            : [];

        var systemPrompt = config.Mode == GroundingRefinementMode.Synthesize
            ? GroundingRefinerPrompts.SynthesizeMode
            : GroundingRefinerPrompts.FilterMode;

        if (!string.IsNullOrWhiteSpace(config.Instructions))
            systemPrompt = systemPrompt + "\n\n" + config.Instructions.Trim();

        var userPrompt = BuildUserPrompt(briefing, sourcesToRefine);
        var tool = config.Mode == GroundingRefinementMode.Synthesize ? SynthesizeTool : FilterTool;

        try
        {
            var (client, model, maxTokens) = llmClientResolver.ForProfile(
                config.Binding.Provider, config.Binding.Model, config.Binding.MaxTokens);

            var response = await client.CompleteAsync(new LlmRequest
            {
                Model = model,
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,
                MaxTokens = maxTokens,
                Tools = [tool],
                ToolChoice = "function:submit_refinement",
            }, ct);

            if (response.FinishReason != "tool_calls" || response.ToolArgumentsJson is null)
            {
                logger.LogWarning(
                    "GroundingRefiner: LLM did not call submit_refinement (finish_reason='{Reason}'); skipping.",
                    response.FinishReason);
                return (raw, new RefinementOutcome(
                    RefinedCitations: raw.Citations,
                    DroppedCitations: [],
                    SynthesizedText: null,
                    WasSkipped: true,
                    SkipReason: $"Refiner LLM did not call submit_refinement (finish_reason='{response.FinishReason}')."));
            }

            // Attempt to record cost regardless of parse outcome.
            var inputTokens = response.TokenUsage.InputTokens;
            var outputTokens = response.TokenUsage.OutputTokens;
            var costEur = pricingCatalog.CalculateCostEur(model, inputTokens, outputTokens, config.Binding.Provider,
                cachedInputTokens: response.TokenUsage.CachedInputTokens ?? 0);

            await costRepository.AddAsync(new GroundingActorCost
            {
                Id = Guid.NewGuid(),
                RunId = runId,
                GroundingProviderName = groundingProviderName,
                ActorName = "GroundingRefiner",
                ProviderName = config.Binding.Provider,
                ModelName = model,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CostEur = costEur,
                CreatedAt = DateTimeOffset.UtcNow,
            }, ct);

            logger.LogInformation(
                "GroundingRefiner: {Mode} pass completed for '{Provider}': {In}→{Out} tokens, model={Model}",
                config.Mode, groundingProviderName, inputTokens, outputTokens, model);

            return config.Mode == GroundingRefinementMode.Synthesize
                ? ParseSynthesizeResult(response.ToolArgumentsJson, raw, sourcesToRefine, overflow)
                : ParseFilterResult(response.ToolArgumentsJson, raw, sourcesToRefine, overflow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "GroundingRefiner: unhandled error during refinement for '{Provider}'; falling back to raw result.",
                groundingProviderName);
            return (raw, new RefinementOutcome(
                RefinedCitations: raw.Citations,
                DroppedCitations: [],
                SynthesizedText: null,
                WasSkipped: true,
                SkipReason: ex.Message));
        }
    }

    private static (GroundingResult Refined, RefinementOutcome Outcome) ParseFilterResult(
        string toolArgumentsJson,
        GroundingResult raw,
        IReadOnlyList<SourceCitation> sourcesToRefine,
        IReadOnlyList<SourceCitation> overflow)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("sources", out var sourcesEl))
                return GracefulDegradation(raw, "Filter tool response missing 'sources' field.");

            var retained = new List<SourceCitation>();
            var dropped = new List<DroppedCitation>();

            foreach (var sourceEl in sourcesEl.EnumerateArray())
            {
                if (!sourceEl.TryGetProperty("index", out var indexEl)) continue;
                var index = indexEl.GetInt32();
                if (index < 0 || index >= sourcesToRefine.Count) continue;

                var original = sourcesToRefine[index];
                var keep = sourceEl.TryGetProperty("keep", out var keepEl) && keepEl.GetBoolean();
                var reason = sourceEl.TryGetProperty("reason", out var reasonEl)
                    ? reasonEl.GetString() ?? string.Empty
                    : string.Empty;

                if (keep)
                {
                    // Use cleaned_snippet when provided, otherwise retain the original snippet.
                    var cleanedSnippet = sourceEl.TryGetProperty("cleaned_snippet", out var snippetEl)
                        ? snippetEl.GetString()
                        : null;

                    var citation = !string.IsNullOrWhiteSpace(cleanedSnippet)
                        ? original with { Snippet = cleanedSnippet }
                        : original;

                    retained.Add(citation);
                }
                else
                {
                    dropped.Add(new DroppedCitation(original, reason));
                }
            }

            // Overflow citations are appended unchanged.
            var allRetained = retained.Concat(overflow).ToList();
            var enrichedContext = BuildFilterEnrichedContext(allRetained);

            var refinedResult = raw with
            {
                Citations = allRetained,
                EnrichedContext = enrichedContext,
            };

            return (refinedResult, new RefinementOutcome(
                RefinedCitations: allRetained,
                DroppedCitations: dropped,
                SynthesizedText: null,
                WasSkipped: false,
                SkipReason: null));
        }
        catch (JsonException ex)
        {
            return GracefulDegradation(raw, $"Failed to parse filter tool response: {ex.Message}");
        }
    }

    private static (GroundingResult Refined, RefinementOutcome Outcome) ParseSynthesizeResult(
        string toolArgumentsJson,
        GroundingResult raw,
        IReadOnlyList<SourceCitation> sourcesToRefine,
        IReadOnlyList<SourceCitation> overflow)
    {
        try
        {
            using var doc = JsonDocument.Parse(toolArgumentsJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("synthesized_text", out var synthesizedEl))
                return GracefulDegradation(raw, "Synthesize tool response missing 'synthesized_text' field.");

            var synthesizedText = synthesizedEl.GetString() ?? string.Empty;

            var referencedIndices = new HashSet<int>();
            if (root.TryGetProperty("referenced_indices", out var refEl))
            {
                foreach (var idx in refEl.EnumerateArray())
                {
                    var i = idx.GetInt32();
                    if (i >= 0 && i < sourcesToRefine.Count)
                        referencedIndices.Add(i);
                    // Out-of-range indices are silently skipped.
                }
            }

            var droppedIndices = new HashSet<int>();
            if (root.TryGetProperty("dropped_indices", out var dropEl))
            {
                foreach (var idx in dropEl.EnumerateArray())
                {
                    var i = idx.GetInt32();
                    if (i >= 0 && i < sourcesToRefine.Count)
                        droppedIndices.Add(i);
                }
            }

            var dropReasons = new Dictionary<string, string>();
            if (root.TryGetProperty("drop_reasons", out var reasonsEl))
            {
                foreach (var prop in reasonsEl.EnumerateObject())
                    dropReasons[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }

            // In Synthesize mode all citations are retained (for reference), but we record which were dropped.
            var refinedCitations = raw.Citations.ToList();

            var dropped = droppedIndices
                .Where(i => i < sourcesToRefine.Count)
                .Select(i =>
                {
                    var reason = dropReasons.TryGetValue(i.ToString(), out var r) ? r : "Excluded by synthesizer.";
                    return new DroppedCitation(sourcesToRefine[i], reason);
                })
                .ToList();

            var enrichedContext = BuildSynthesizeEnrichedContext(synthesizedText, sourcesToRefine, overflow);

            var refinedResult = raw with
            {
                Citations = refinedCitations,
                EnrichedContext = enrichedContext,
            };

            return (refinedResult, new RefinementOutcome(
                RefinedCitations: refinedCitations,
                DroppedCitations: dropped,
                SynthesizedText: synthesizedText,
                WasSkipped: false,
                SkipReason: null));
        }
        catch (JsonException ex)
        {
            return GracefulDegradation(raw, $"Failed to parse synthesize tool response: {ex.Message}");
        }
    }

    private static (GroundingResult, RefinementOutcome) GracefulDegradation(GroundingResult raw, string reason)
        => (raw, new RefinementOutcome(
            RefinedCitations: raw.Citations,
            DroppedCitations: [],
            SynthesizedText: null,
            WasSkipped: true,
            SkipReason: reason));

    private static string BuildUserPrompt(string briefing, IReadOnlyList<SourceCitation> sources)
    {
        var sb = new StringBuilder();
        sb.Append("Briefing: ").AppendLine(briefing).AppendLine();
        sb.Append("Sources to review (").Append(sources.Count).AppendLine(" total):");

        for (var i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            sb.Append('[').Append(i).AppendLine("]");
            sb.Append("    Title: ").AppendLine(s.Title);

            if (!string.IsNullOrWhiteSpace(s.Url))
                sb.Append("    URL: ").AppendLine(s.Url);
            else if (!string.IsNullOrWhiteSpace(s.DocumentReference))
                sb.Append("    DocumentReference: ").AppendLine(s.DocumentReference);

            sb.Append("    Snippet: ").AppendLine(s.Snippet);

            if (s.RelevanceScore.HasValue)
                sb.Append("    Relevance: ").AppendLine(s.RelevanceScore.Value.ToString("F3"));

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string BuildFilterEnrichedContext(IReadOnlyList<SourceCitation> citations)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < citations.Count; i++)
        {
            var c = citations[i];
            sb.Append('[').Append(i + 1).Append("] ").AppendLine(c.Title);
            sb.AppendLine(c.Snippet);
        }
        return sb.ToString();
    }

    private static string BuildSynthesizeEnrichedContext(
        string synthesizedText,
        IReadOnlyList<SourceCitation> refinedSources,
        IReadOnlyList<SourceCitation> overflow)
    {
        var sb = new StringBuilder();
        sb.AppendLine("[Synthesized Context]");
        sb.AppendLine(synthesizedText);
        sb.AppendLine();
        sb.AppendLine("[Original Sources (for reference)]");

        var allSources = refinedSources.Concat(overflow).ToList();
        for (var i = 0; i < allSources.Count; i++)
        {
            var s = allSources[i];
            sb.Append('[').Append(i).Append("] Title: ").AppendLine(s.Title);
            sb.Append("    Snippet: ").AppendLine(s.Snippet);
        }

        return sb.ToString();
    }
}
