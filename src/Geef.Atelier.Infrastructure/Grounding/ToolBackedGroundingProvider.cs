using Geef.Atelier.Application.Crew.Grounding;
using Geef.Atelier.Application.Tools;
using Geef.Atelier.Core.Domain.Crew.Grounding;
using Geef.Atelier.Core.Domain.Tools;
using Geef.Atelier.Core.Persistence.Tools;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Infrastructure.Grounding;

/// <summary>
/// Generic grounding provider that delegates execution to <see cref="IToolExecutor"/>
/// using a central <see cref="ToolDefinition"/>. This is the "new path" provider that
/// replaces per-type <see cref="IGroundingProvider"/> implementations when a profile
/// specifies a <see cref="GroundingProviderProfile.ToolName"/>.
/// </summary>
internal sealed class ToolBackedGroundingProvider(
    IToolExecutor toolExecutor,
    IToolDefinitionRepository toolDefinitionRepository,
    ILogger<ToolBackedGroundingProvider> logger) : IGroundingProvider
{
    // This provider uses a special sentinel type — it is not dispatched by ProviderType
    // but instead used when a GroundingProviderProfile has a ToolName set.
    public string ProviderType => "__tool-backed__";

    /// <inheritdoc/>
    public async Task<GroundingResult> EnrichAsync(
        string briefingText,
        GroundingProviderProfile profile,
        Guid runId,
        CancellationToken ct)
    {
        var toolName = profile.ToolName
            ?? throw new InvalidOperationException(
                $"ToolBackedGroundingProvider requires GroundingProviderProfile.ToolName to be set (profile: {profile.Name}).");

        var tool = await toolDefinitionRepository.GetByNameAsync(toolName, ct)
            ?? throw new InvalidOperationException(
                $"Tool '{toolName}' not found in catalog (referenced by grounding profile: {profile.Name}).");

        // Build input JSON: for search types, use the briefingText as query;
        // static-context tools need no input.
        var inputJson = tool.ToolType switch
        {
            ToolType.StaticContext => "{}",
            _ => System.Text.Json.JsonSerializer.Serialize(new { query = briefingText })
        };

        var ctx = new ToolInvocationContext(
            RunId: runId,
            IterationNumber: 0,
            ActorType: "grounding",
            ActorName: profile.Name,
            Sequence: 0);

        var result = await toolExecutor.ExecuteAsync(tool, inputJson, ctx, ct);

        if (result.Error is not null)
        {
            logger.LogWarning(
                "ToolBackedGroundingProvider: tool={ToolName} run={RunId} error={Error}",
                toolName, runId, result.Error);
            return new GroundingResult(
                ProviderName: profile.Name,
                EnrichedContext: string.Empty,
                Citations: [],
                TokensOrCreditsUsed: 0,
                CostEur: null);
        }

        return new GroundingResult(
            ProviderName: profile.Name,
            EnrichedContext: result.Output ?? string.Empty,
            Citations: [],
            TokensOrCreditsUsed: 0,
            CostEur: result.CostEur);
    }
}
